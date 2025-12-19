using UnityEngine;

[DisallowMultipleComponent]
public class CrosshairAim : MonoBehaviour
{
    public enum E_AimResolveMode
    {
        RaycastToGround = 0,
        PlaneIntersection = 1,
    }

    [Header("Refs")]
    [SerializeField] private Camera _camera;

    [Header("Crosshair Resolve")]
    [SerializeField] private E_AimResolveMode _resolveMode = E_AimResolveMode.RaycastToGround;
    [SerializeField] private LayerMask _groundMask = ~0;
    [SerializeField] private float _rayMaxDistance = 500f;

    public Camera Camera => _camera;

    private void Awake()
    {
        if (_camera == null) _camera = Camera.main;
    }

    public Vector3 GetCameraForwardFlat()
    {
        if (_camera == null) return Vector3.forward;
        Vector3 f = _camera.transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }

    /// <summary>
    /// 크로스헤어 = 카메라 중앙. 월드 포인트(바닥 히트 또는 평면 교차) 계산.
    /// </summary>
    public bool TryGetCrosshairWorldPoint(float planeY, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (_camera == null) return false;

        Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (_resolveMode == E_AimResolveMode.RaycastToGround)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, _rayMaxDistance, _groundMask, QueryTriggerInteraction.Ignore))
            {
                worldPoint = hit.point;
                return true;
            }
            return false;
        }

        Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            worldPoint = ray.GetPoint(enter);
            return true;
        }
        return false;
    }
}
