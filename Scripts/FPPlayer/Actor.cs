using UnityEngine;

public class Actor : MonoBehaviour, IDamageable
{
    public int Health = 100;
    private bool isCrit = false;

    public void ApplyDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, object source = null)
    {
        Health -= (int)amount;
        if (amount >= 20)
        {
            isCrit = true;
        }

        if (amount != 0)
        {
            GetComponent<DamageTextSpawner>()?.ShowDamage((int)amount, isCrit);
        }



        if (Health <= 0f)
        {
            Die();
        }

        isCrit = false;
    }

    public void TakeDamage(float amount)
    {
        Health -= (int)amount;
        if (amount >= 20)
        {
            isCrit = true;
        }

        if (amount != 0)
        {
            GetComponent<DamageTextSpawner>()?.ShowDamage((int)amount, isCrit);
        }



        if (Health <= 0f)
        {
            Die();
        }

        isCrit = false;
    }

    void Die()
    {
        // Play death animation, destroy object, etc.
        Destroy(gameObject);
    }
}
