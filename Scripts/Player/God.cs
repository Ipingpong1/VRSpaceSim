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

    [Header("Trajectory Prediction")]
    public int predictionSteps = 500;
    public float predictionTimeStep = 0.5f;
    public Material trajectoryDefaultMaterial;
    public Material trajectoryCollisionMaterial;

    [Header("Debug")]
    public float velocityRayScale = 1f;

    [Header("Planet Generation")]
    [SerializeField] private float ringRadiusStep = 1000f;
    [SerializeField] private float Planet_Velocity = 3f;

    [Header("Planet Prefabs (index 0 = Sun, 1 = Mercury, 2 = Venus ...)")]
    public GameObject sunPrefab;
    public GameObject mercuryPrefab;
    public GameObject venusPrefab;
    public GameObject earthPrefab;
    public GameObject marsPrefab;
    public GameObject jupiterPrefab;
    public GameObject saturnPrefab;
    public GameObject uranusPrefab;
    public GameObject neptunePrefab;

    [Header("Runtime State (Play Mode)")]
    public Vector3 currentVelocity;
    public Vector3 currentPosition;

    [Header("God View")]
    [SerializeField] public float godWidth = 5f;
    public bool godViewActive;

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

    void Start()
    {
        currentPosition = initialPosition;
        currentVelocity = initialVelocity;

        trajectoryLine = GetComponent<LineRenderer>();
        trajectoryLine.positionCount = predictionSteps;
        trajectoryLine.useWorldSpace = true;
        trajectoryLine.startWidth = .1f;
        trajectoryLine.endWidth = .1f;
        trajectoryLine.widthMultiplier = godViewActive ? godWidth : 1f;

        planets = new Planet[9];

        // sun
        planets[0].radius = 700f;
        planets[0].mass = 5f;
        planets[0].orbitalRadius = 0f;
        planets[0].rotationSpeed = 6f;
        planets[0].position = Vector3.zero;

        // mercury
        planets[1].radius = 50f;
        planets[1].mass = 1f;
        planets[1].orbitalRadius = 1500f;
        planets[1].rotationSpeed = 6f;
        planets[1].orbitalAngle = 300f;
        planets[1].position = new Vector3(planets[1].orbitalRadius * Mathf.Cos(planets[1].orbitalAngle), 0f, planets[1].orbitalRadius * Mathf.Sin(planets[1].orbitalAngle));

        // venus
        planets[2].radius = 100f;
        planets[2].mass = 1.5f;
        planets[2].orbitalRadius = 2500f;
        planets[2].rotationSpeed = 6f;
        planets[2].orbitalAngle = 20f;
        planets[2].position = new Vector3(planets[2].orbitalRadius * Mathf.Cos(planets[2].orbitalAngle), 0f, planets[2].orbitalRadius * Mathf.Sin(planets[2].orbitalAngle));

        // earth
        planets[3].radius = 200f;
        planets[3].mass = 2f;
        planets[3].orbitalRadius = 4200f;
        planets[3].rotationSpeed = 6f;
        planets[3].orbitalAngle = 1.72f;
        planets[3].position = new Vector3(planets[3].orbitalRadius * Mathf.Cos(planets[3].orbitalAngle), 0f, planets[3].orbitalRadius * Mathf.Sin(planets[3].orbitalAngle));

        // mars
        planets[4].radius = 100f;
        planets[4].mass = 0.5f;
        planets[4].orbitalRadius = 5800f;
        planets[4].rotationSpeed = 6f;
        planets[4].orbitalAngle = 80f;
        planets[4].position = new Vector3(planets[4].orbitalRadius * Mathf.Cos(planets[4].orbitalAngle), 0f, planets[4].orbitalRadius * Mathf.Sin(planets[4].orbitalAngle));

        // jupiter
        planets[5].radius = 500f;
        planets[5].mass = 10f;
        planets[5].orbitalRadius = 8000f;
        planets[5].rotationSpeed = 6f;
        planets[5].orbitalAngle = 100f;
        planets[5].position = new Vector3(planets[5].orbitalRadius * Mathf.Cos(planets[5].orbitalAngle), 0f, planets[5].orbitalRadius * Mathf.Sin(planets[5].orbitalAngle));

        // saturn
        planets[6].radius = 400f;
        planets[6].mass = 8f;
        planets[6].orbitalRadius = 9500f;
        planets[6].rotationSpeed = 6f;
        planets[6].orbitalAngle = 150f;
        planets[6].position = new Vector3(planets[6].orbitalRadius * Mathf.Cos(planets[6].orbitalAngle), 0f, planets[6].orbitalRadius * Mathf.Sin(planets[6].orbitalAngle));

        // uranus
        planets[7].radius = 250f;
        planets[7].mass = 5f;
        planets[7].orbitalRadius = 20000f;
        planets[7].rotationSpeed = 6f;
        planets[7].orbitalAngle = 200f;
        planets[7].position = new Vector3(planets[7].orbitalRadius * Mathf.Cos(planets[7].orbitalAngle), 0f, planets[7].orbitalRadius * Mathf.Sin(planets[7].orbitalAngle));

        // neptune
        planets[8].radius = 250f;
        planets[8].mass = 5f;
        planets[8].orbitalRadius = 26000f;
        planets[8].rotationSpeed = 6f;
        planets[8].orbitalAngle = 250f;
        planets[8].position = new Vector3(planets[8].orbitalRadius * Mathf.Cos(planets[8].orbitalAngle), 0f, planets[8].orbitalRadius * Mathf.Sin(planets[8].orbitalAngle));

        planetVisuals = new Transform[planets.Length];
        string[] planetNames = { "Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
        for (int i = 0; i < planets.Length; i++)
        {
            string planetName = (i < planetNames.Length) ? planetNames[i] : $"Planet_{i}";
            GameObject planetObject = GameObject.Find(planetName);
            if (planetObject == null)
                Debug.LogWarning($"Planet GameObject '{planetName}' not found in scene.");
            planetVisuals[i] = planetObject != null ? planetObject.transform : null;
        }

        _trajectoryPoints = new Vector3[predictionSteps];
        _ghostPlanets = new Planet[planets.Length];
    }

    void Update()
    {
        // Floating origin: the ship sits at world (0,0,0) permanently.
        // Planet visuals are placed at (simPosition - shipSimPosition) so they
        // appear at the correct distance without Unity ever seeing large coordinates.
        float simTimeSinceFixed = (Time.time - Time.fixedTime) * timeScale;
        for (int i = 0; i < planets.Length; i++)
        {
            if (planetVisuals[i] == null) continue;

            Vector3 simPos;
            if (planets[i].orbitalRadius >= 0.001f)
            {
                float angVel = Planet_Velocity / planets[i].orbitalRadius;
                float visualAngle = planets[i].orbitalAngle + angVel * simTimeSinceFixed;
                simPos = new Vector3(
                    Mathf.Cos(visualAngle) * planets[i].orbitalRadius,
                    0f,
                    Mathf.Sin(visualAngle) * planets[i].orbitalRadius
                );
            }
            else
            {
                simPos = planets[i].position; // sun stays at sim origin
            }

            planetVisuals[i].position = simPos - currentPosition;
            planetVisuals[i].Rotate(Vector3.up, planets[i].rotationSpeed * Time.deltaTime, Space.World);
        }

        trajectoryLine.widthMultiplier = godViewActive ? godWidth : 1f;

        // Trajectory point 0 is always the ship, which is always at world origin.
        if (trajectoryLine.positionCount > 0)
            trajectoryLine.SetPosition(0, Vector3.zero);
    }

    void FixedUpdate()
    {
        int stepCount = Mathf.Max(1, substeps);
        float frameDt = Time.fixedDeltaTime * timeScale;
        float stepDt = frameDt / stepCount;

        for (int i = 0; i < stepCount; i++)
        {
            AdvancePlanets(ref planets, stepDt);
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

        DrawTrajectory();
    }

    void FindClosestPlanet(Vector3 p, Planet[] planetArray)
    {
        float minDistance = float.MaxValue;
        for (int i = 0; i < planetArray.Length; i++)
        {
            float dist = Vector3.Distance(p, planetArray[i].position);
            if (dist < minDistance)
            {
                minDistance = dist;
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

        bool willCollide = false;
        int pointCount = 0;

        for (int i = 0; i < predictionSteps; i++)
        {
            _trajectoryPoints[i] = ghostPos;
            pointCount++;

            for (int j = 0; j < _ghostPlanets.Length; j++)
            {
                if (Vector3.Distance(ghostPos, _ghostPlanets[j].position) < _ghostPlanets[j].radius)
                {
                    willCollide = true;
                    break;
                }
            }
            if (willCollide) break;

            AdvancePlanets(ref _ghostPlanets, predictionTimeStep);
            CalculateRK4(ref ghostPos, ref ghostVel, ref _ghostPlanets, predictionTimeStep);
        }

        // Convert simulation-space points to world space (floating origin)
        for (int i = 0; i < pointCount; i++)
            _trajectoryPoints[i] -= currentPosition;

        trajectoryLine.positionCount = pointCount;
        trajectoryLine.SetPositions(_trajectoryPoints);

        if (willCollide && trajectoryCollisionMaterial != null)
            trajectoryLine.material = trajectoryCollisionMaterial;
        else if (trajectoryDefaultMaterial != null)
            trajectoryLine.material = trajectoryDefaultMaterial;
    }

    Vector3 GetAcceleration(Vector3 p, Planet[] planetArray)
    {
        Vector3 totalAcceleration = Vector3.zero;
        for (int i = 0; i < planetArray.Length; i++)
        {
            Vector3 toPlanet = planetArray[i].position - p;
            if (toPlanet.magnitude > gravityEffectRadius)
                continue;

            float rSq = Vector3.Dot(toPlanet, toPlanet);
            if (rSq < 0.0001f) continue;

            float rCubed = rSq * Mathf.Sqrt(rSq);
            totalAcceleration += (toPlanet / rCubed) * gravityMultiplier * planetArray[i].mass;
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

    void AdvancePlanets(ref Planet[] planetArray, float dt)
    {
        for (int i = 0; i < planetArray.Length; i++)
        {
            if (planetArray[i].orbitalRadius < 0.001f) continue;

            float angularVelocity = Planet_Velocity / planetArray[i].orbitalRadius;
            planetArray[i].orbitalAngle += angularVelocity * dt;

            planetArray[i].position = new Vector3(
                Mathf.Cos(planetArray[i].orbitalAngle) * planetArray[i].orbitalRadius,
                0f,
                Mathf.Sin(planetArray[i].orbitalAngle) * planetArray[i].orbitalRadius
            );
        }
    }
}
