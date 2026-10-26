using UnityEngine;

namespace SamProduction
{
    public sealed class SamDamageable : MonoBehaviour
    {
        public float maximumHealth = 1000;
        public float Health { get; private set; }
        public int ReceivedHits { get; private set; }
        private Vector3 restScale;
        private float hitTime = -10;
        private void Awake() { Health = maximumHealth; restScale = transform.localScale; }
        public void Receive(float amount, Vector3 direction)
        {
            Health = Mathf.Max(0, Health - amount);
            ReceivedHits++;
            hitTime = Time.time;
        }
        public void ResetTarget()
        {
            Health = maximumHealth;
            ReceivedHits = 0;
            hitTime = -10;
            transform.localScale = restScale;
        }
        private void Update()
        {
            float impulse = Mathf.Exp(-(Time.time - hitTime) * 18);
            transform.localScale = Vector3.Scale(restScale, new Vector3(1 + impulse * .06f, 1 - impulse * .08f, 1 + impulse * .06f));
        }
    }
}
