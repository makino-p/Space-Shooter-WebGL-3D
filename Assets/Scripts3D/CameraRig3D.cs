using UnityEngine;

// Камера от третьего лица за кораблём, с плавным следованием и лёгкой тряской.
public class CameraRig3D : MonoBehaviour
{
    public Vector3 Offset = new Vector3(0f, 3.2f, -12f);
    public float FollowSharpness = 6f;
    public float BaseFov = 65f;
    public float BoostFov = 78f;

    private Transform _target;
    private Camera _camera;
    private float _shakeTime;
    private float _shakeDuration;
    private float _shakeMagnitude;

    public void SetTarget(Transform target)
    {
        _target = target;
        if (_target != null)
        {
            transform.position = _target.TransformPoint(Offset);
            transform.rotation = _target.rotation;
        }
    }

    void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (_target == null) return;

        float k = 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime);
        Vector3 desiredPos = _target.TransformPoint(Offset);
        transform.position = Vector3.Lerp(transform.position, desiredPos, k);

        Vector3 lookPoint = _target.position + _target.forward * 25f;
        Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, _target.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, k);

        Player3D player = _target.GetComponent<Player3D>();
        if (_camera != null && player != null)
        {
            float fov = player.IsBoosting ? BoostFov : BaseFov;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, Time.deltaTime * 4f);
        }

        if (_shakeTime > 0f)
        {
            _shakeTime -= Time.deltaTime;
            float strength = _shakeMagnitude * Mathf.Clamp01(_shakeTime / _shakeDuration);
            transform.position += transform.right * Random.Range(-1f, 1f) * strength
                                + transform.up * Random.Range(-1f, 1f) * strength;
        }
    }

    public void Shake(float duration, float magnitude)
    {
        if (magnitude >= _shakeMagnitude * Mathf.Clamp01(_shakeTime / Mathf.Max(_shakeDuration, 0.001f)))
        {
            _shakeDuration = duration;
            _shakeTime = duration;
            _shakeMagnitude = magnitude;
        }
    }
}
