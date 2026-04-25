using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class RK4Orbit : MonoBehaviour
{
    public GameObject ship;

    [Header("Initial Conditions (Unity Units)")]
    public Vector3 initialPosition = new Vector3(100f, 100f, 100f);
    public Vector3 initialVelocity = new Vector3(0f, 0f, 5f);

    [Header("Simulation Settings")]
    public float timeScale = 1f;
    public float gravityMultiplier = 1f;
    public int substeps = 4;
    public float gravityEffectRadius = 5000f;
    [Tooltip("Distance from a planet centre that triggers planet-relative trajectory drawing.")]
    public float clampDistance = 5000f;
    [Tooltip("Leapfrog is symplectic (conserves orbital energy) — recommended for stable orbit capture. RK4 is more accurate for short maneuvers but orbits slowly spiral over time.")]
    public bool useLeapfrog = true;

    [Header("Trajectory Prediction")]
    public int predictionSteps = 500;
    public float predictionTimeStep = 0.5f;
    [Tooltip("RK4 sub-steps per trajectory segment. Higher = more accurate path, more CPU.")]
    public int predictionSubsteps = 4;
    public Material trajectoryDefaultMaterial;
    public Material trajectoryCollisionMaterial;

    [Header("Sun Light")]
    [Tooltip("Assign a Point or Directional light here. Position/rotation will be updated each frame to match the sun. Configure intensity, shadows, and range in the Light component itself.")]
    public Light sunLight;

    [Header("Debug")]
    public float velocityRayScale = 1f;

    [Header("Planet Generation")]
    [SerializeField] private float ringRadiusStep = 1000f;
    [SerializeField] private float Planet_Velocity = 948.7f;

    [SerializeField] private float AU = 20000f;
    [SerializeField] public float baseMass = 1f;

    [Header("Runtime State (Play Mode)")]
    public Vector3 currentVelocity;
    public Vector3 currentPosition;

    [Header("God View")]
    public bool godViewActive;
    [Tooltip("How much to compress the solar system in god view. Tune until the whole system fits comfortably in front of you.")]
    [SerializeField] private float godViewScale = 0.00005f;
    [Tooltip("World-space position the miniature solar system is centered on (relative to ship / world origin).")]
    [SerializeField] private Vector3 godViewCenter = new Vector3(0f, -0.5f, 2f);
    [Tooltip("Trajectory line width in normal view.")]
    [SerializeField] private float normalLineWidth = 1f;
    [Tooltip("Trajectory line width in god view (world units at miniature scale, e.g. 0.02).")]
    [SerializeField] private float godViewLineWidth = 0.02f;
    [Tooltip("Marker object shown at the ship's position inside the miniature. Assign any mesh (sphere, small ship model, etc.)")]
    public GameObject shipMarker;
    [Tooltip("Size of the ship marker in the miniature. Larger than planets so it's easy to spot.")]
    [SerializeField] private float godViewShipMarkerScale = 0.05f;

    [Header("Trajectory Line")]
    [Tooltip("Line width at the start (near the ship) in normal view.")]
    [SerializeField] private float trajectoryStartWidth = 0.1f;
    [Tooltip("Line width at the end (far from ship) in normal view — wider makes it visible at distance.")]
    [SerializeField] private float trajectoryEndWidth = 5f;

    [Header("Orbit Paths (God View)")]
    public Material orbitPathMaterial;
    [Tooltip("Line width of orbit dashes in god view world units.")]
    [SerializeField] private float orbitLineWidth = 0.003f;
    [Tooltip("Number of dashes per orbit circle.")]
    [SerializeField] private int orbitDashCount = 30;
    [Tooltip("Fraction of each dash slot that is solid (0–1). 0.5 = equal dash and gap.")]
    [SerializeField] private float orbitDashFraction = 0.5f;

    private LineRenderer trajectoryLine;

    public struct Planet
    {
        public Vector3 position;
        public float mass;
        public float radius;
        public float orbitalAngle;
        public float orbitalRadius;
        public float rotationSpeed;
    }

    private Planet[] planets;
    private Transform[] planetVisuals;
    private Planet closestPlanet;

    // Orbital Elements
    private Vector3 r;
    private float r_mag;
    private float nu;
    private float a;
    private float e;
    private Vector3 eVector;

    // Cached arrays — allocated once in Start to avoid per-frame GC pressure
    private Vector3[] _trajectoryPoints;
    private Planet[] _ghostPlanets;
    // Original planet scales stored so god view can compress and restore them cleanly
    private Vector3[] _originalPlanetScales;
    // Cached ship renderers — avoids GetComponentsInChildren allocation every Update
    private Renderer[] _shipRenderers;

    // Orbit path dash renderers — [planet][dash]
    private LineRenderer[][] _orbitDashRenderers;
    // Pre-computed orbit circle points in simulation space — [planet][dash][point]
    private Vector3[][][] _orbitSimPoints;
    // Reusable buffer for SetPositions calls
    private Vector3[] _dashPointBuffer;

    // Planet sim positions cached each Update so visuals and clamp rings share one calculation
    private Vector3[] _planetSimPositions;

    // Clamp-distance ring dash renderers per planet — [planet][dash]
    private LineRenderer[][] _clampRingRenderers;
    // Clamp ring dash points in PLANET-LOCAL XZ space (offset by planet sim pos at render time)
    private Vector3[][][] _clampRingLocalPoints;

    // Second trajectory line in planet-relative frame, shown only when within clampDistance of a planet's surface
    private LineRenderer _planetFrameTrajectoryLine;
    private Vector3[] _planetFrameTrajectoryPoints;
    private Material _planetFrameMaterial;

    void Start()
    {
        currentPosition = initialPosition;
        currentVelocity = initialVelocity;

        trajectoryLine = GetComponent<LineRenderer>();
        trajectoryLine.positionCount = predictionSteps;
        trajectoryLine.useWorldSpace = true;
        trajectoryLine.startWidth = trajectoryStartWidth;
        trajectoryLine.endWidth = trajectoryEndWidth;
        trajectoryLine.widthMultiplier = 1f;

        planets = new Planet[9];

        // sun
        planets[0].radius = 700f;
        planets[0].mass = 20f;
        planets[0].orbitalRadius = 0f;
        planets[0].rotationSpeed = 6f;
        planets[0].position = Vector3.zero;

        // mercury
        planets[1].radius = 50f;
        planets[1].mass = 1f;
        planets[1].orbitalRadius = AU * 1f;
        planets[1].rotationSpeed = 6f;
        planets[1].orbitalAngle = 300f;
        planets[1].position = new Vector3(planets[1].orbitalRadius * Mathf.Cos(planets[1].orbitalAngle), 0f, planets[1].orbitalRadius * Mathf.Sin(planets[1].orbitalAngle));

        // venus
        planets[2].radius = 100f;
        planets[2].mass = 1.5f;
        planets[2].orbitalRadius = AU * 2f;
        planets[2].rotationSpeed = 6f;
        planets[2].orbitalAngle = 20f;
        planets[2].position = new Vector3(planets[2].orbitalRadius * Mathf.Cos(planets[2].orbitalAngle), 0f, planets[2].orbitalRadius * Mathf.Sin(planets[2].orbitalAngle));

        // earth
        planets[3].radius = 200f;
        planets[3].mass = 3f;
        planets[3].orbitalRadius = AU * 5f;
        planets[3].rotationSpeed = 6f;
        planets[3].orbitalAngle = 1.72f;
        planets[3].position = new Vector3(planets[3].orbitalRadius * Mathf.Cos(planets[3].orbitalAngle), 0f, planets[3].orbitalRadius * Mathf.Sin(planets[3].orbitalAngle));

        // mars
        planets[4].radius = 100f;
        planets[4].mass = 1f;
        planets[4].orbitalRadius = AU * 10f;
        planets[4].rotationSpeed = 6f;
        planets[4].orbitalAngle = 80f;
        planets[4].position = new Vector3(planets[4].orbitalRadius * Mathf.Cos(planets[4].orbitalAngle), 0f, planets[4].orbitalRadius * Mathf.Sin(planets[4].orbitalAngle));

        // jupiter
        planets[5].radius = 500f;
        planets[5].mass = 10f;
        planets[5].orbitalRadius = AU * 14.0f;
        planets[5].rotationSpeed = 6f;
        planets[5].orbitalAngle = 100f;
        planets[5].position = new Vector3(planets[5].orbitalRadius * Mathf.Cos(planets[5].orbitalAngle), 0f, planets[5].orbitalRadius * Mathf.Sin(planets[5].orbitalAngle));

        // saturn
        planets[6].radius = 400f;
        planets[6].mass = 8f;
        planets[6].orbitalRadius = AU * 15.537f;
        planets[6].rotationSpeed = 6f;
        planets[6].orbitalAngle = 150f;
        planets[6].position = new Vector3(planets[6].orbitalRadius * Mathf.Cos(planets[6].orbitalAngle), 0f, planets[6].orbitalRadius * Mathf.Sin(planets[6].orbitalAngle));

        // uranus
        planets[7].radius = 250f;
        planets[7].mass = 5f;
        planets[7].orbitalRadius = AU * 19.191f;
        planets[7].rotationSpeed = 6f;
        planets[7].orbitalAngle = 200f;
        planets[7].position = new Vector3(planets[7].orbitalRadius * Mathf.Cos(planets[7].orbitalAngle), 0f, planets[7].orbitalRadius * Mathf.Sin(planets[7].orbitalAngle));

        // neptune
        planets[8].radius = 250f;
        planets[8].mass = 5f;
        planets[8].orbitalRadius = AU * 30.069f;
        planets[8].rotationSpeed = 6f;
        planets[8].orbitalAngle = 250f;
        planets[8].position = new Vector3(planets[8].orbitalRadius * Mathf.Cos(planets[8].orbitalAngle), 0f, planets[8].orbitalRadius * Mathf.Sin(planets[8].orbitalAngle));

        planetVisuals = new Transform[planets.Length];
        _originalPlanetScales = new Vector3[planets.Length];
        string[] planetNames = { "Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
        for (int i = 0; i < planets.Length; i++)
        {
            string planetName = (i < planetNames.Length) ? planetNames[i] : $"Planet_{i}";
            GameObject planetObject = GameObject.Find(planetName);
            if (planetObject == null)
                Debug.LogWarning($"Planet GameObject '{planetName}' not found in scene.");
            planetVisuals[i] = planetObject != null ? planetObject.transform : null;

            if (planetVisuals[i] != null)
            {
                // Radius driven by scene object scale so physics boundary matches visuals
                planets[i].radius = planetVisuals[i].localScale.x;
                // Cache original scale so god view can compress and restore correctly,
                // preserving any non-uniform axes (e.g. Saturn's ring squash)
                _originalPlanetScales[i] = planetVisuals[i].localScale;
            }
            else
            {
                _originalPlanetScales[i] = Vector3.one * planets[i].radius;
            }
        }

        // Spawn just outside Earth's surface (+700 units clearance) along the +X axis
        Vector3 earthSimPos = new Vector3(
            Mathf.Cos(planets[3].orbitalAngle) * planets[3].orbitalRadius,
            0f,
            Mathf.Sin(planets[3].orbitalAngle) * planets[3].orbitalRadius
        );
        currentPosition = earthSimPos + new Vector3(planets[3].radius + 700f, 0f, 0f);

        _trajectoryPoints = new Vector3[predictionSteps];
        _ghostPlanets = new Planet[planets.Length];
        _planetSimPositions = new Vector3[planets.Length];
        _planetFrameTrajectoryPoints = new Vector3[predictionSteps];

        if (ship != null)
            _shipRenderers = ship.GetComponentsInChildren<Renderer>();

        if (sunLight != null)
        {
            // Register as URP's main directional light so it's treated as the
            // scene sun and works regardless of Additional Lights pipeline settings
            sunLight.type = LightType.Directional;
            RenderSettings.sun = sunLight;
        }

        // Pre-compute orbit circle points and build dash LineRenderers
        const int kDashSegments = 3; // points per dash arc (start, mid, end)
        _dashPointBuffer = new Vector3[kDashSegments];
        _orbitSimPoints = new Vector3[planets.Length][][];
        _orbitDashRenderers = new LineRenderer[planets.Length][];

        for (int i = 1; i < planets.Length; i++) // skip Sun (index 0, orbitalRadius == 0)
        {
            if (planets[i].orbitalRadius < 0.001f) continue;

            float slotAngle = 2f * Mathf.PI / orbitDashCount;
            float dashAngle = slotAngle * orbitDashFraction;

            _orbitSimPoints[i] = new Vector3[orbitDashCount][];
            _orbitDashRenderers[i] = new LineRenderer[orbitDashCount];

            for (int d = 0; d < orbitDashCount; d++)
            {
                // Pre-compute kDashSegments points along this dash arc
                _orbitSimPoints[i][d] = new Vector3[kDashSegments];
                float startAngle = d * slotAngle;
                for (int s = 0; s < kDashSegments; s++)
                {
                    float t = (kDashSegments > 1) ? (float)s / (kDashSegments - 1) : 0f;
                    float angle = startAngle + dashAngle * t;
                    _orbitSimPoints[i][d][s] = new Vector3(
                        Mathf.Cos(angle) * planets[i].orbitalRadius,
                        0f,
                        Mathf.Sin(angle) * planets[i].orbitalRadius
                    );
                }

                // Create child object with LineRenderer for this dash
                GameObject dashObj = new GameObject($"OrbitDash_{i}_{d}");
                dashObj.transform.SetParent(transform);
                LineRenderer lr = dashObj.AddComponent<LineRenderer>();
                lr.sharedMaterial = orbitPathMaterial;
                lr.useWorldSpace = true;
                lr.loop = false;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.startWidth = orbitLineWidth;
                lr.endWidth = orbitLineWidth;
                lr.positionCount = kDashSegments;
                lr.gameObject.SetActive(false);
                _orbitDashRenderers[i][d] = lr;
            }
        }

        // Clamp-distance rings — gray dashes at radius (clampDistance + planet.radius)
        // around each non-sun planet. Stored in PLANET-LOCAL space; offset by current
        // planet sim pos at render time so the ring tracks the orbiting planet.
        _clampRingLocalPoints = new Vector3[planets.Length][][];
        _clampRingRenderers = new LineRenderer[planets.Length][];

        for (int i = 1; i < planets.Length; i++) // skip Sun
        {
            float ringRadius = clampDistance + planets[i].radius;
            float slotAngle = 2f * Mathf.PI / orbitDashCount;
            float dashAngle = slotAngle * orbitDashFraction;

            _clampRingLocalPoints[i] = new Vector3[orbitDashCount][];
            _clampRingRenderers[i] = new LineRenderer[orbitDashCount];

            for (int d = 0; d < orbitDashCount; d++)
            {
                _clampRingLocalPoints[i][d] = new Vector3[kDashSegments];
                float startAngle = d * slotAngle;
                for (int s = 0; s < kDashSegments; s++)
                {
                    float t = (kDashSegments > 1) ? (float)s / (kDashSegments - 1) : 0f;
                    float angle = startAngle + dashAngle * t;
                    _clampRingLocalPoints[i][d][s] = new Vector3(
                        Mathf.Cos(angle) * ringRadius,
                        0f,
                        Mathf.Sin(angle) * ringRadius
                    );
                }

                GameObject dashObj = new GameObject($"ClampRing_{i}_{d}");
                dashObj.transform.SetParent(transform);
                LineRenderer lr = dashObj.AddComponent<LineRenderer>();
                lr.sharedMaterial = orbitPathMaterial;
                lr.useWorldSpace = true;
                lr.loop = false;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.startWidth = orbitLineWidth;
                lr.endWidth = orbitLineWidth;
                lr.positionCount = kDashSegments;
                lr.gameObject.SetActive(false);
                _clampRingRenderers[i][d] = lr;
            }
        }

        // Second trajectory LineRenderer for planet-relative frame, tinted cyan
        GameObject planetFrameObj = new GameObject("PlanetFrameTrajectory");
        planetFrameObj.transform.SetParent(transform);
        _planetFrameTrajectoryLine = planetFrameObj.AddComponent<LineRenderer>();
        _planetFrameTrajectoryLine.useWorldSpace = true;
        _planetFrameTrajectoryLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _planetFrameTrajectoryLine.receiveShadows = false;
        _planetFrameTrajectoryLine.positionCount = predictionSteps;
        _planetFrameTrajectoryLine.startWidth = trajectoryStartWidth;
        _planetFrameTrajectoryLine.endWidth = trajectoryEndWidth;
        _planetFrameTrajectoryLine.gameObject.SetActive(false);

        if (trajectoryDefaultMaterial != null)
        {
            _planetFrameMaterial = new Material(trajectoryDefaultMaterial);
            Color cyan = Color.cyan;
            // Set both URP and legacy color properties — source shader may use either
            if (_planetFrameMaterial.HasProperty("_BaseColor")) _planetFrameMaterial.SetColor("_BaseColor", cyan);
            if (_planetFrameMaterial.HasProperty("_Color"))     _planetFrameMaterial.SetColor("_Color", cyan);
            _planetFrameTrajectoryLine.material = _planetFrameMaterial;
        }
    }

    void Update()
    {
        // In god view the solar system compresses into a miniature in front of the player.
        // In normal view displayScale=1 and displayCenter=zero, so the math is identical
        // to the standard floating-origin placement.
        float displayScale  = godViewActive ? godViewScale  : 1f;
        Vector3 displayCenter = godViewActive ? godViewCenter : Vector3.zero;

        if (_shipRenderers != null)
            foreach (var r in _shipRenderers)
                r.enabled = !godViewActive;

        if (shipMarker != null)
        {
            shipMarker.SetActive(godViewActive);
            if (godViewActive)
            {
                // Ship is always the origin of the miniature — sits exactly at godViewCenter
                shipMarker.transform.position = godViewCenter;
                shipMarker.transform.localScale = Vector3.one * godViewShipMarkerScale;
            }
        }

        // Width: flat/equal in god view (tiny world units), tapered in normal view
        ApplyTrajectoryWidth(trajectoryLine);
        if (_planetFrameTrajectoryLine != null)
            ApplyTrajectoryWidth(_planetFrameTrajectoryLine);

        float simTimeSinceFixed = (Time.time - Time.fixedTime) * timeScale;
        for (int i = 0; i < planets.Length; i++)
        {
            Vector3 simPos;
            if (planets[i].orbitalRadius >= 0.001f)
            {
                float angVel = Planet_Velocity / Mathf.Pow(planets[i].orbitalRadius, 1.5f);
                float visualAngle = planets[i].orbitalAngle + angVel * simTimeSinceFixed;
                simPos = new Vector3(
                    Mathf.Cos(visualAngle) * planets[i].orbitalRadius,
                    0f,
                    Mathf.Sin(visualAngle) * planets[i].orbitalRadius
                );
            }
            else
            {
                simPos = planets[i].position;
            }
            _planetSimPositions[i] = simPos;

            if (planetVisuals[i] == null) continue;

            // Position: compressed around the miniature center in god view
            planetVisuals[i].position = (simPos - currentPosition) * displayScale + displayCenter;
            // Scale: uniform compression preserving any non-uniform axes (rings, etc.)
            planetVisuals[i].localScale = _originalPlanetScales[i] * displayScale;
            planetVisuals[i].Rotate(Vector3.up, planets[i].rotationSpeed * Time.deltaTime, Space.World);
        }

        // Ship is always at world origin; in god view the trajectory arc
        // starts at the miniature center instead.
        if (trajectoryLine.positionCount > 0)
            trajectoryLine.SetPosition(0, displayCenter);

        // Sun light — position Point Light at sun world pos, or aim Directional Light from sun toward ship
        if (sunLight != null)
        {
            Vector3 sunWorldPos = (planets[0].position - currentPosition) * displayScale + displayCenter;
            if (sunLight.type == LightType.Directional)
            {
                Vector3 shipWorldPos = godViewActive ? godViewCenter : Vector3.zero;
                Vector3 toShip = shipWorldPos - sunWorldPos;
                if (toShip.sqrMagnitude > 0.0001f)
                    sunLight.transform.rotation = Quaternion.LookRotation(toShip.normalized);
            }
            else
            {
                sunLight.transform.position = sunWorldPos;
            }
        }

        // Orbit path dashes — only visible in god view
        for (int i = 1; i < planets.Length; i++)
        {
            if (_orbitDashRenderers[i] == null) continue;
            for (int d = 0; d < orbitDashCount; d++)
            {
                LineRenderer lr = _orbitDashRenderers[i][d];
                lr.gameObject.SetActive(godViewActive);
                if (!godViewActive) continue;

                for (int s = 0; s < _dashPointBuffer.Length; s++)
                    _dashPointBuffer[s] = (_orbitSimPoints[i][d][s] - currentPosition) * displayScale + displayCenter;
                lr.SetPositions(_dashPointBuffer);
            }
        }

        // Clamp-distance rings — gray dashes at (clampDistance + radius) around each planet, god-view only
        for (int i = 1; i < planets.Length; i++)
        {
            if (_clampRingRenderers == null || _clampRingRenderers[i] == null) continue;
            for (int d = 0; d < orbitDashCount; d++)
            {
                LineRenderer lr = _clampRingRenderers[i][d];
                lr.gameObject.SetActive(godViewActive);
                if (!godViewActive) continue;

                for (int s = 0; s < _dashPointBuffer.Length; s++)
                {
                    Vector3 simPoint = _planetSimPositions[i] + _clampRingLocalPoints[i][d][s];
                    _dashPointBuffer[s] = (simPoint - currentPosition) * displayScale + displayCenter;
                }
                lr.SetPositions(_dashPointBuffer);
            }
        }
    }

    void ApplyTrajectoryWidth(LineRenderer lr)
    {
        if (godViewActive)
        {
            lr.startWidth = godViewLineWidth;
            lr.endWidth   = godViewLineWidth;
        }
        else
        {
            lr.startWidth = trajectoryStartWidth;
            lr.endWidth   = trajectoryEndWidth;
        }
    }

    void FixedUpdate()
    {
        int stepCount = Mathf.Max(1, substeps);
        float frameDt = Time.fixedDeltaTime * timeScale;
        float stepDt = frameDt / stepCount;

        for (int i = 0; i < stepCount; i++)
        {
            AdvancePlanets(ref planets, stepDt);
            if (useLeapfrog)
                CalculateLeapfrog(ref currentPosition, ref currentVelocity, ref planets, stepDt);
            else
                CalculateRK4(ref currentPosition, ref currentVelocity, ref planets, stepDt);
        }

        FindClosestPlanet(currentPosition, planets);

        // Orbital elements
        r = currentPosition - closestPlanet.position;
        r_mag = r.magnitude;
        nu = gravityMultiplier * closestPlanet.mass;

        if (r_mag > 0.0001f && nu > 0.0001f)
        {
            float vSq = currentVelocity.sqrMagnitude;
            float rv = Vector3.Dot(r, currentVelocity);
            a = 1f / ((2f / r_mag) - (vSq / nu));
            eVector = ((vSq - (nu / r_mag)) * r - (rv * currentVelocity)) / nu;
            e = eVector.magnitude;
        }
        else
        {
            a = 0f;
            eVector = Vector3.zero;
            e = 0f;
        }

        if (ship != null)
        {
            ship.transform.position = Vector3.zero; // ship is always the world origin
            Debug.DrawRay(Vector3.zero, currentVelocity * velocityRayScale, Color.cyan);
            Debug.DrawRay(Vector3.zero, (closestPlanet.position - currentPosition).normalized * velocityRayScale * 10, Color.yellow);
        }
        Debug.Log(currentVelocity.magnitude);
        DrawTrajectory();
    }

    void FindClosestPlanet(Vector3 p, Planet[] planetArray)
    {
        float minDistSq = float.MaxValue;
        for (int i = 0; i < planetArray.Length; i++)
        {
            float distSq = (p - planetArray[i].position).sqrMagnitude;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                closestPlanet = planetArray[i];
            }
        }
    }

    void DrawTrajectory()
    {
        Vector3 ghostPos = currentPosition;
        Vector3 ghostVel = currentVelocity;

        for (int i = 0; i < planets.Length; i++)
            _ghostPlanets[i] = planets[i];

        // Find closest planet whose SURFACE is within clampDistance — used to enable
        // the secondary planet-frame trajectory line (does not affect the world line)
        int soiIdx = -1;
        float soiMinSurfaceDist = clampDistance;
        for (int i = 1; i < planets.Length; i++) // skip sun
        {
            float surfaceDist = (currentPosition - planets[i].position).magnitude - planets[i].radius;
            if (surfaceDist < soiMinSurfaceDist) { soiMinSurfaceDist = surfaceDist; soiIdx = i; }
        }

        bool willCollide = false;
        int pointCount = 0;

        for (int i = 0; i < predictionSteps; i++)
        {
            _trajectoryPoints[i] = ghostPos; // world-frame (always recorded)
            if (soiIdx >= 0)
                _planetFrameTrajectoryPoints[i] = ghostPos - _ghostPlanets[soiIdx].position;
            pointCount++;

            for (int j = 0; j < _ghostPlanets.Length; j++)
            {
                float r = _ghostPlanets[j].radius;
                if ((ghostPos - _ghostPlanets[j].position).sqrMagnitude < r * r)
                {
                    willCollide = true;
                    break;
                }
            }
            if (willCollide) break;

            // Sub-step each trajectory segment so prediction accuracy matches the main sim
            int sub = Mathf.Max(1, predictionSubsteps);
            float subDt = predictionTimeStep / sub;
            for (int s = 0; s < sub; s++)
            {
                AdvancePlanets(ref _ghostPlanets, subDt);
                if (useLeapfrog)
                    CalculateLeapfrog(ref ghostPos, ref ghostVel, ref _ghostPlanets, subDt);
                else
                    CalculateRK4(ref ghostPos, ref ghostVel, ref _ghostPlanets, subDt);
            }
        }

        // Convert simulation-space points to world space, applying the same
        // scale+center transform that planet visuals use in god view
        float displayScale  = godViewActive ? godViewScale  : 1f;
        Vector3 displayCenter = godViewActive ? godViewCenter : Vector3.zero;

        // World-frame line — always shown, anchored at ship (currentPosition → displayCenter)
        for (int i = 0; i < pointCount; i++)
            _trajectoryPoints[i] = (_trajectoryPoints[i] - currentPosition) * displayScale + displayCenter;

        trajectoryLine.positionCount = pointCount;
        trajectoryLine.SetPositions(_trajectoryPoints);

        if (willCollide && trajectoryCollisionMaterial != null)
            trajectoryLine.material = trajectoryCollisionMaterial;
        else if (trajectoryDefaultMaterial != null)
            trajectoryLine.material = trajectoryDefaultMaterial;

        // Planet-frame line — anchored at planet world position, only when within clamp surface distance
        if (_planetFrameTrajectoryLine != null)
        {
            bool show = soiIdx >= 0;
            _planetFrameTrajectoryLine.gameObject.SetActive(show);
            if (show)
            {
                Vector3 planetWorldPos = (planets[soiIdx].position - currentPosition) * displayScale + displayCenter;
                for (int i = 0; i < pointCount; i++)
                    _planetFrameTrajectoryPoints[i] = _planetFrameTrajectoryPoints[i] * displayScale + planetWorldPos;
                _planetFrameTrajectoryLine.positionCount = pointCount;
                _planetFrameTrajectoryLine.SetPositions(_planetFrameTrajectoryPoints);
            }
        }
    }

    Vector3 GetAcceleration(Vector3 p, Planet[] planetArray)
    {
        Vector3 totalAcceleration = Vector3.zero;
        for (int i = 0; i < planetArray.Length; i++)
        {
            Vector3 toPlanet = planetArray[i].position - p;
            if (Vector3.Dot(toPlanet, toPlanet) > gravityEffectRadius * gravityEffectRadius)
                continue;

            float rSq = Vector3.Dot(toPlanet, toPlanet);
            if (rSq < 0.0001f) continue;

            float rCubed = rSq * Mathf.Sqrt(rSq);
            totalAcceleration += (toPlanet / rCubed) * gravityMultiplier * planetArray[i].mass * baseMass;
        }
        return totalAcceleration;
    }

    void CalculateRK4(ref Vector3 p, ref Vector3 v, ref Planet[] planetArray, float dt)
    {
        float half_dt = dt * 0.5f;

        Vector3 k1_r = v;
        Vector3 k1_v = GetAcceleration(p, planetArray);

        Vector3 k2_r = v + k1_v * half_dt;
        Vector3 k2_v = GetAcceleration(p + k1_r * half_dt, planetArray);

        Vector3 k3_r = v + k2_v * half_dt;
        Vector3 k3_v = GetAcceleration(p + k2_r * half_dt, planetArray);

        Vector3 k4_r = v + k3_v * dt;
        Vector3 k4_v = GetAcceleration(p + k3_r * dt, planetArray);

        float dt6 = dt / 6f;

        p += dt6 * (k1_r + 2f * k2_r + 2f * k3_r + k4_r);
        v += dt6 * (k1_v + 2f * k2_v + 2f * k3_v + k4_v);
    }

    void CalculateLeapfrog(ref Vector3 p, ref Vector3 v, ref Planet[] planetArray, float dt)
    {
        // Kick-drift-kick (velocity Verlet) — symplectic, conserves orbital energy
        v += GetAcceleration(p, planetArray) * (dt * 0.5f); // half-kick
        p += v * dt;                                         // drift
        v += GetAcceleration(p, planetArray) * (dt * 0.5f); // half-kick
    }

    void AdvancePlanets(ref Planet[] planetArray, float dt)
    {
        for (int i = 0; i < planetArray.Length; i++)
        {
            if (planetArray[i].orbitalRadius < 0.001f) continue;

            float angularVelocity = Planet_Velocity / Mathf.Pow(planetArray[i].orbitalRadius, 1.5f);
            planetArray[i].orbitalAngle += angularVelocity * dt;

            planetArray[i].position = new Vector3(
                Mathf.Cos(planetArray[i].orbitalAngle) * planetArray[i].orbitalRadius,
                0f,
                Mathf.Sin(planetArray[i].orbitalAngle) * planetArray[i].orbitalRadius
            );
        }
    }
}
