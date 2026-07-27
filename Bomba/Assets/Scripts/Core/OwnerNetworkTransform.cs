using Unity.Netcode.Components;
using UnityEngine;

namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Oyuncu karakterinin hareketini sahibi olan istemcinin yazmasını sağlar.
    /// NetworkTransform ayarı prefab yeniden kaydedildiğinde değişmesin diye
    /// yetki modeli kod seviyesinde sabitlenmiştir.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
