using UnityEngine;
using UnityEngine.UI;
using UMol.API;

namespace UMol {

/// <summary>
/// Panel VR flotante para controlar iluminación (intensidad de luz direccional,
/// luz ambiente, sombras). La jerarquía UI es un prefab construido en el Editor
/// (ver EffectsPanelVR.prefab); este script solo contiene el cableado de los
/// sliders con la API de UnityMol.
/// </summary>
public class EffectsPanelVR : MonoBehaviour {

    [SerializeField] Slider lightSlider;
    [SerializeField] Slider ambientSlider;
    [SerializeField] Slider shadowSlider;

    [SerializeField] Text lightValueText;
    [SerializeField] Text ambientValueText;
    [SerializeField] Text shadowValueText;

    void Start() {
        WireSlider(lightSlider,   lightValueText,   v => APIPython.setDirLightIntensity(v));
        WireSlider(ambientSlider, ambientValueText, v => APIPython.setAmbientLightIntensity(v));
        WireSlider(shadowSlider,  shadowValueText,  v => APIPython.setDirLightShadow(v));
    }

    void WireSlider(Slider sl, Text valText, System.Action<float> onChange) {
        if (sl == null) return;
        sl.onValueChanged.AddListener(v => {
            if (valText != null) valText.text = v.ToString("F2");
            onChange(v);
        });
    }
}
}
