 using System;
 using UnityEngine;
 using Random = UnityEngine.Random;
 
 [RequireComponent(typeof (Light))]
 public class TorchLight : MonoBehaviour {
 
     public float MinLightIntensity = 0.6F;
     public float MaxLightIntensity = 1.0F;
 
     public float AccelerateTime = 0.15f;
 
     private float _targetIntensity = 1.0f;
     private float _lastIntensity = 1.0f;
 
     private float _timePassed = 0.0f;
 
     private Light _lt;
     private const double Tolerance = 0.0001;
 
     private Vector2 v = Vector2.zero;

     public float time = 3f;
     public float range = 10f;
     public float speed = 10f;

     public int seed = 0;

     private void Start() 
     {
         Random.seed = seed;

         _lt = GetComponent<Light>();
         _lastIntensity = _lt.intensity;

          _lt.color = Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);
         FixedUpdate();

         v1 = _lt.transform.position;

          float randomAngle = Random.Range(0f, Mathf.PI * 2f);
          v = new Vector2(Mathf.Sin(randomAngle), Mathf.Cos(randomAngle));

          v.Normalize();
     }
 
     float moveTime = 0;
     Vector3 v1;
     private void FixedUpdate() {
         _timePassed += Time.deltaTime;
         _lt.intensity = Mathf.Lerp(_lastIntensity, _targetIntensity, _timePassed/AccelerateTime);
         _lt.transform.position += new Vector3(v.x, 0, v.y) * Time.deltaTime * speed;

         if (Math.Abs(_lt.intensity - _targetIntensity) < Tolerance) {
             _lastIntensity = _lt.intensity;
             _targetIntensity = Random.Range(MinLightIntensity, MaxLightIntensity);
             _timePassed = 0.0f;
         }
         Vector2 vv = new Vector2(v1.x, v1.z);
         Vector2 vp = new Vector2(_lt.transform.position.x,_lt.transform.position.z);
         if(Vector2.Distance(vp, vv) > range)
         {
            v = Vector2.Reflect(v, (vv - vp).normalized); 
         }
         else
         {   
             moveTime += Time.deltaTime;
             if(moveTime > time)
             {
                  moveTime = 0;
                //v = new Vector2(Random.value * 2f - 1f, Random.value* 2f - 1f);
              float randomAngle = Random.Range(0f, Mathf.PI * 2f);
              v = new Vector2(Mathf.Sin(randomAngle), Mathf.Cos(randomAngle));
            }
         }

         v.Normalize();
     }
 }