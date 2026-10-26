using UnityEngine;

namespace SamProduction
{
    public sealed class SamStageLayout : MonoBehaviour
    {
        public GameObject referenceBlockout;
        public Renderer neutralFloor;
        public Camera view;
        public Material referenceSky;
        Material previousSky;
        CameraClearFlags previousClear;
        bool referenceVisible;
        public void SetReferenceVisible(bool visible)
        {
            if (visible == referenceVisible) return;
            if (visible)
            {
                previousSky=RenderSettings.skybox;
                if(view!=null){previousClear=view.clearFlags;view.clearFlags=CameraClearFlags.Skybox;}
                if(referenceSky!=null)RenderSettings.skybox=referenceSky;
            }
            else
            {
                RenderSettings.skybox=previousSky;
                if(view!=null)view.clearFlags=previousClear;
            }
            referenceVisible=visible;
            if (referenceBlockout != null) referenceBlockout.SetActive(visible);
            if (neutralFloor != null) neutralFloor.enabled = !visible;
        }
    }
}
