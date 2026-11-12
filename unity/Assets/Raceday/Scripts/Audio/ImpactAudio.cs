using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car's hits and scrapes: a hit as it meets a barrier or another car, louder and heavier the faster they close
    /// (<see cref="Impacts"/>), and a scrape held while it grinds along one. Sits on the car's body, beside its
    /// <see cref="CarController"/>, so it hears the same collisions. Two cars touching each hear it; the one with the
    /// lower id plays it.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class ImpactAudio : MonoBehaviour
    {
        /// <summary>Least time between two hits from one car, s, unless the later is much louder.</summary>
        private const float HitGap = 0.12f;
        /// <summary>How long a scrape carries on after the last physics step that felt it, s.</summary>
        private const float ScrapeHold = 0.1f;
        private const float HitNear = 8f, HitFar = 320f, ScrapeNear = 6f, ScrapeFar = 200f;

        private float lastHit = float.NegativeInfinity, lastVolume;
        private float scrapeAt = float.NegativeInfinity, scrapeSpeed;
        private HitKind scrapeKind;
        private Vector3 scrapePoint;

        private void OnCollisionEnter(Collision collision)
        {
            if (!Kind(collision, out HitKind kind)) return;
            float closing = 0f;
            Vector3 point = transform.position;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint c = collision.GetContact(i);
                float into = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, c.normal));
                if (into > closing) { closing = into; point = c.point; }
            }
            if (!Impacts.Of(kind, closing, out Impacts.Hit hit)) return;
            if (Time.time - lastHit < HitGap && hit.Volume < lastVolume * 1.5f) return;
            lastHit = Time.time;
            lastVolume = hit.Volume;
            // Harder hits sit a little lower: more of the car's mass in them.
            float pitch = 1f - 0.08f * Mathf.InverseLerp(Impacts.Heavy, Impacts.Loudest, closing);
            RaceSfx.Play(hit.Bank, point, hit.Volume, HitNear, HitFar, pitch);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!Kind(collision, out HitKind kind) || kind == HitKind.Tyres || collision.contactCount == 0) return;
            ContactPoint c = collision.GetContact(0);
            Vector3 v = collision.relativeVelocity;
            float sliding = (v - c.normal * Vector3.Dot(v, c.normal)).magnitude;
            if (sliding < Impacts.ScrapeFrom) return;
            scrapeAt = Time.time;
            scrapeSpeed = sliding;
            scrapeKind = kind;
            scrapePoint = c.point;
        }

        private void Update()
        {
            if (Time.time - scrapeAt > ScrapeHold) return;
            if (Impacts.Scrape(scrapeKind, scrapeSpeed, out float volume, out float pitch))
                RaceSfx.Hold(this, SfxBank.Scrape, scrapePoint, volume, pitch, ScrapeNear, ScrapeFar);
        }

        /// <summary>What the car met, if it makes a sound: a barrier, or another car this car speaks for.</summary>
        private bool Kind(Collision collision, out HitKind kind)
        {
            kind = HitKind.Hard;
            Collider other = collision.collider;
            if (other.attachedRigidbody != null && other.attachedRigidbody.TryGetComponent(out ImpactAudio them))
            {
                kind = HitKind.Car;
                return GetInstanceID() < them.GetInstanceID();
            }
            if (!other.TryGetComponent(out TrackSurface surface) || surface.Kind != SurfaceKind.Barrier) return false;
            if (other.name == "Tyre walls") kind = HitKind.Tyres;
            return true;
        }
    }
}
