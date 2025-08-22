using PurrNet;
using UnityEngine;

public class TestNetwork : NetworkIdentity
{
    [SerializeField] private NetworkIdentity _networkIdentity;
    [SerializeField] private Color _color;
    [SerializeField] private Renderer _renderer;

    [SerializeField] private SyncVar<int> _health = new(100);

    /* protected override void OnSpawned()
    {
        base.OnSpawned();

        if (!isServer)
        {
            return;
        }

        Instantiate(_networkIdentity, Vector3.zero, Quaternion.identity);
    } */

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            SetColor(_color);
        }
        if (Input.GetKeyDown(KeyCode.F))
        {
            TakeDamage(10);
        }
    }

    [ObserversRpc(bufferLast: true)]

    private void SetColor(Color color)
    {
        _renderer.material.color = color;
    }

    [ServerRpc]
    private void TakeDamage(int damage)
    {
        _health.value -= damage;
    }
}
