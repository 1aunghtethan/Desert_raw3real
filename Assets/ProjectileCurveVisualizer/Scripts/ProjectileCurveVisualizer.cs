using System.Collections.Generic;
using UnityEngine;

namespace ProjectileCurveVisualizerSystem
{
    public class ProjectileCurveVisualizer : MonoBehaviour
    {
        private LineRenderer lineRenderer;

        public Transform projectileTargetPlaneTransform;
        private MeshRenderer projectileTargetPlaneMeshRenderer;

        public LayerMask ignoredLayers;
        private Transform ignoredCollisionRoot;
        private readonly HashSet<Collider> ignoredColliders = new HashSet<Collider>();

        public int curveSubdivision = 32;
        public float maximumInAirTime = 6.0f;
        public float detectionInterval = 0.5f;
        public float gravity = 9.81f;

        public bool getHitObjectTransform = false;
        public bool calculateProjectileVelocityWhenHit = false;

        [Header("Visual Settings")]
        public Color startColor = Color.white;
        public Color endColor = Color.white;
        public float startWidth = 0.15f;
        public float endWidth = 0.01f;
        public Color hitMarkerColor = Color.red;

        private Vector3 horizontalVector;
        private float horizontalDisplacement;
        private float horizontalTime;

        // Projectile variables
        private Vector3 previousDetectionPosition = Vector3.zero;
        private Vector3 nextDetectionPosition = Vector3.zero;
        private float t;
        public List<Vector3> detectionPositionList = new List<Vector3>();
        private Collider[] hitColliderArray = new Collider[16];
        private bool notHit = true;
        private RaycastHit defaultRaycastHit;
        private Vector3 rayDirection;
        private float rayLength;
        public Vector3 hitPosition;
        private Vector3 hitNormal;

        private float predictedProjectileTravelTime;
        private Vector3 horizontalDirection;
        private float horizontalDistance;
        private float deltaHeight;
        private float launchSpeedSquare;
        private float component;
        private float launchAngle;

        // Bézier curve variables
        private Vector3 startPoint = Vector3.zero;
        private Vector3 endPoint = Vector3.zero;
        private Vector3 controlPoint = Vector3.zero;

        public Transform hitObjectTransform;
        public Vector3 projectileVelocityWhenHit;

        void Awake()
        {
            // Initialize variables
            lineRenderer = GetComponent<LineRenderer>();
            lineRenderer.positionCount = curveSubdivision;
            lineRenderer.startColor = startColor;
            lineRenderer.endColor = endColor;
            lineRenderer.startWidth = startWidth;
            lineRenderer.endWidth = endWidth;

            projectileTargetPlaneMeshRenderer = projectileTargetPlaneTransform.GetComponent<MeshRenderer>();
            if (projectileTargetPlaneMeshRenderer != null)
            {
                projectileTargetPlaneMeshRenderer.material.color = hitMarkerColor;
            }

            defaultRaycastHit = new RaycastHit();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(hitPosition, 0.25f);
        }

        public void VisualizeProjectileCurve(Vector3 projectileStartPosition, float projectileStartPositionForwardOffset, Vector3 launchVelocity, float projectileRadius, float distanceOffsetAboveHitPosition, bool debugMode, out Vector3 updatedProjectileStartPosition, out RaycastHit hit)
        {
            hit = defaultRaycastHit;

            hitObjectTransform = null;

            if (!lineRenderer.enabled)
                lineRenderer.enabled = true;

            t = 0.0f;

            updatedProjectileStartPosition = projectileStartPosition + new Vector3(launchVelocity.x, 0.0f, launchVelocity.z).normalized * projectileStartPositionForwardOffset;

            previousDetectionPosition = updatedProjectileStartPosition;

            detectionPositionList = new List<Vector3>();

            notHit = true;

            while (t < maximumInAirTime)
            {
                // Move the detection sphere along the projectile curve
                nextDetectionPosition = updatedProjectileStartPosition + new Vector3(launchVelocity.x * t, launchVelocity.y * t - 0.5f * gravity * t * t, launchVelocity.z * t);
                t += detectionInterval;

                detectionPositionList.Add(nextDetectionPosition);

                // Perform ray physics detection for each detection position pair, check whether there is obstacle blocked between them
                rayDirection = (nextDetectionPosition - previousDetectionPosition).normalized;
                rayLength = Vector3.Distance(previousDetectionPosition, nextDetectionPosition);

                if (debugMode)
                    Debug.DrawLine(previousDetectionPosition, previousDetectionPosition + rayDirection * rayLength, Color.green);

                if (TryRaycastNonIgnored(previousDetectionPosition, rayDirection, rayLength, out hit))
                {
                    notHit = false;

                    hitPosition = hit.point;
                    hitNormal = hit.normal;

                    if (getHitObjectTransform)
                        hitObjectTransform = hit.transform;

                    if (calculateProjectileVelocityWhenHit)
                    {
                        horizontalVector = new Vector3(launchVelocity.x, 0.0f, launchVelocity.z);
                        horizontalDisplacement = Vector3.Distance(new Vector3(previousDetectionPosition.x, 0.0f, previousDetectionPosition.z), new Vector3(hitPosition.x, 0.0f, hitPosition.z));
                        horizontalTime = horizontalDisplacement / horizontalVector.magnitude;

                        projectileVelocityWhenHit = (hitPosition - previousDetectionPosition) / horizontalTime;
                    }

                    if (debugMode)
                        Debug.DrawLine(hitPosition, hitPosition + hitNormal, Color.red);

                    break;
                }
                else
                {
                    // Perform sphere physics detection at current position, check whether there is obstacle on either side of the curve
                    if (TryOverlapSphereNonIgnored(nextDetectionPosition, projectileRadius, out Collider col))
                    {
                        notHit = false;

                        if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mc && mc.convex))
                        {
                            hitPosition = col.ClosestPoint(nextDetectionPosition);
                        }
                        else
                        {
                            hitPosition = col.bounds.ClosestPoint(nextDetectionPosition);
                        }
                        hitNormal = Vector3.Normalize(nextDetectionPosition - hitPosition);
                        if (hitNormal.sqrMagnitude <= 0.0001f)
                            hitNormal = Vector3.up;

                        break;
                    }
                }

                previousDetectionPosition = nextDetectionPosition;
            }

            startPoint = updatedProjectileStartPosition;

            if (notHit)
            {
                endPoint = detectionPositionList[detectionPositionList.Count - 1];

                if (projectileTargetPlaneMeshRenderer.enabled)
                    projectileTargetPlaneMeshRenderer.enabled = false;
            }
            else
            {
                endPoint = hitPosition;

                projectileTargetPlaneTransform.position = hitPosition;
                projectileTargetPlaneTransform.LookAt(projectileTargetPlaneTransform.position + hitNormal);
                projectileTargetPlaneTransform.rotation = Quaternion.Euler(projectileTargetPlaneTransform.rotation.eulerAngles.x + 90.0f, projectileTargetPlaneTransform.rotation.eulerAngles.y, projectileTargetPlaneTransform.rotation.eulerAngles.z);

                projectileTargetPlaneTransform.position += hitNormal * distanceOffsetAboveHitPosition;

                if (!projectileTargetPlaneMeshRenderer.enabled)
                    projectileTargetPlaneMeshRenderer.enabled = true;
            }

            if (detectionPositionList.Count > 2)
                controlPoint = 2.0f * detectionPositionList[detectionPositionList.Count / 2] - startPoint * 0.5f - endPoint * 0.5f;
            else if (detectionPositionList.Count == 2)
                controlPoint = (startPoint + endPoint) / 2.0f;

            // Render Bézier curve
            lineRenderer.SetPosition(0, startPoint);
            lineRenderer.SetPosition(lineRenderer.positionCount - 1, endPoint);

            // The following t is a parameter Bézier curve, not the time t above
            t = 0.0f;
            for (int i = 0; i < lineRenderer.positionCount; i++)
            {
                lineRenderer.SetPosition(i, (1 - t) * (1 - t) * startPoint + 2 * (1 - t) * t * controlPoint + t * t * endPoint);
                t += (1 / (float)lineRenderer.positionCount);
            }
        }

        public bool VisualizeProjectileCurveWithTargetPosition(Vector3 projectileStartPosition, float projectileStartPositionForwardOffset, Vector3 projectileEndPosition, float launchSpeed, Vector3 throwerVelocity, Vector3 targetObjectVelocity, float projectileRadius, float distanceOffsetAboveHitPosition, bool debugMode, out Vector3 updatedProjectileStartPosition, out Vector3 projectileLaunchVelocity, out Vector3 predictedTargetPosition, out RaycastHit hit)
        {
            hit = defaultRaycastHit;

            // Use the target object velocity to predict the end position
            predictedProjectileTravelTime = (Vector3.Distance(projectileStartPosition, projectileEndPosition) - projectileStartPositionForwardOffset) / launchSpeed;
            predictedTargetPosition = projectileEndPosition + (targetObjectVelocity - throwerVelocity) * predictedProjectileTravelTime;

            horizontalDirection = predictedTargetPosition - projectileStartPosition;
            horizontalDirection.y = 0.0f;
            horizontalDirection = horizontalDirection.normalized;

            updatedProjectileStartPosition = projectileStartPosition + horizontalDirection * projectileStartPositionForwardOffset;
            projectileLaunchVelocity = Vector3.zero;

            horizontalDistance = Vector3.Distance(new Vector3(updatedProjectileStartPosition.x, 0.0f, updatedProjectileStartPosition.z), new Vector3(predictedTargetPosition.x, 0.0f, predictedTargetPosition.z));
            deltaHeight = predictedTargetPosition.y - updatedProjectileStartPosition.y;

            launchSpeedSquare = launchSpeed * launchSpeed;
            component = launchSpeedSquare * launchSpeedSquare - gravity * (gravity * horizontalDistance * horizontalDistance + 2.0f * deltaHeight * launchSpeedSquare);

            if (component < 0.0f)
                return false;

            component = Mathf.Sqrt(component);

            // Only calculate the result with lower angle
            launchAngle = Mathf.Atan2(launchSpeedSquare - component, gravity * horizontalDistance);

            projectileLaunchVelocity = horizontalDirection * Mathf.Cos(launchAngle) * launchSpeed + Vector3.up * Mathf.Sin(launchAngle) * launchSpeed;

            // Perform physics detection after obtaining the launch velocity
            if (!lineRenderer.enabled)
                lineRenderer.enabled = true;

            t = 0.0f;

            previousDetectionPosition = updatedProjectileStartPosition;

            detectionPositionList = new List<Vector3>();

            while (t < maximumInAirTime)
            {
                // Move the detection sphere along the projectile curve
                nextDetectionPosition = updatedProjectileStartPosition + new Vector3(projectileLaunchVelocity.x * t, projectileLaunchVelocity.y * t - 0.5f * gravity * t * t, projectileLaunchVelocity.z * t);
                t += detectionInterval;

                detectionPositionList.Add(nextDetectionPosition);

                // Perform ray physics detection for each detection position pair, check whether there is obstacle blocked between them
                rayDirection = (nextDetectionPosition - previousDetectionPosition).normalized;
                rayLength = Vector3.Distance(previousDetectionPosition, nextDetectionPosition);

                if (debugMode)
                    Debug.DrawLine(previousDetectionPosition, previousDetectionPosition + rayDirection * rayLength, Color.green);

                if (TryRaycastNonIgnored(previousDetectionPosition, rayDirection, rayLength, out hit))
                {
                    notHit = false;

                    hitPosition = hit.point;
                    hitNormal = hit.normal;

                    if (debugMode)
                        Debug.DrawLine(hitPosition, hitPosition + hitNormal, Color.red);

                    break;
                }
                else
                {
                    // Perform sphere physics detection at current position, check whether there is obstacle on either side of the curve
                    if (TryOverlapSphereNonIgnored(nextDetectionPosition, projectileRadius, out Collider col))
                    {
                        notHit = false;

                        if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mc && mc.convex))
                        {
                            hitPosition = col.ClosestPoint(nextDetectionPosition);
                        }
                        else
                        {
                            hitPosition = col.bounds.ClosestPoint(nextDetectionPosition);
                        }
                        hitNormal = Vector3.Normalize(nextDetectionPosition - hitPosition);
                        if (hitNormal.sqrMagnitude <= 0.0001f)
                            hitNormal = Vector3.up;

                        break;
                    }
                }

                previousDetectionPosition = nextDetectionPosition;
            }

            startPoint = updatedProjectileStartPosition;

            endPoint = hitPosition;

            projectileTargetPlaneTransform.position = hitPosition;
            projectileTargetPlaneTransform.LookAt(projectileTargetPlaneTransform.position + hitNormal);
            projectileTargetPlaneTransform.rotation = Quaternion.Euler(projectileTargetPlaneTransform.rotation.eulerAngles.x + 90.0f, projectileTargetPlaneTransform.rotation.eulerAngles.y, projectileTargetPlaneTransform.rotation.eulerAngles.z);

            projectileTargetPlaneTransform.position += hitNormal * distanceOffsetAboveHitPosition;

            if (!projectileTargetPlaneMeshRenderer.enabled)
                projectileTargetPlaneMeshRenderer.enabled = true;

            if (detectionPositionList.Count > 2)
                controlPoint = 2.0f * detectionPositionList[detectionPositionList.Count / 2] - startPoint * 0.5f - endPoint * 0.5f;
            else if (detectionPositionList.Count == 2)
                controlPoint = (startPoint + endPoint) / 2.0f;

            // Render Bézier curve
            lineRenderer.SetPosition(0, startPoint);
            lineRenderer.SetPosition(lineRenderer.positionCount - 1, endPoint);

            // The following t is a parameter Bézier curve, not the time t above
            t = 0.0f;
            for (int i = 0; i < lineRenderer.positionCount; i++)
            {
                lineRenderer.SetPosition(i, (1 - t) * (1 - t) * startPoint + 2 * (1 - t) * t * controlPoint + t * t * endPoint);
                t += (1 / (float)lineRenderer.positionCount);
            }

            return true;
        }

        public void HideProjectileCurve()
        {
            if (lineRenderer.enabled)
            {
                lineRenderer.enabled = false;
                projectileTargetPlaneMeshRenderer.enabled = false;
            }
        }

        public void SetIgnoredCollisionRoot(Transform root)
        {
            ignoredCollisionRoot = root;
            ignoredColliders.Clear();

            if (ignoredCollisionRoot == null)
                return;

            foreach (Collider col in ignoredCollisionRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col != null)
                    ignoredColliders.Add(col);
            }
        }

        private bool TryRaycastNonIgnored(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = defaultRaycastHit;

            RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, ~ignoredLayers, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
                return false;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit candidate in hits)
            {
                if (IsColliderIgnored(candidate.collider))
                    continue;

                hit = candidate;
                return true;
            }

            return false;
        }

        private bool TryOverlapSphereNonIgnored(Vector3 position, float radius, out Collider hitCollider)
        {
            hitCollider = null;

            int hitCount = Physics.OverlapSphereNonAlloc(position, radius, hitColliderArray, ~ignoredLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = hitColliderArray[i];
                if (IsColliderIgnored(candidate))
                    continue;

                hitCollider = candidate;
                return true;
            }

            return false;
        }

        private bool IsColliderIgnored(Collider col)
        {
            if (col == null)
                return false;

            if (ignoredColliders.Contains(col))
                return true;

            return ignoredCollisionRoot != null && col.transform.IsChildOf(ignoredCollisionRoot);
        }
    }
}
