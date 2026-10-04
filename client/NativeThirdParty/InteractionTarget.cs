using UnityEngine;
namespace RootMotion.FinalIK
{
    public class InteractionTarget : MonoBehaviour
    {
        [System.Serializable] public class Multiplier { public int curve; public float multiplier; }
        public int effectorType;
        public Multiplier[] multipliers = new Multiplier[0];
        public float interactionSpeedMlp = 1;
        public Transform pivot;
        public int rotationMode;
        public Vector3 twistAxis = Vector3.up;
        public float twistWeight = 1, swingWeight, threeDOFWeight = 1;
        public bool rotateOnce = true, usePoser = true;
    }
}
