using System.Collections;
using BriefcaseProtocol.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BriefcaseProtocol.Tests.PlayMode
{
    public sealed class RuntimeSmokeTests
    {
        [UnityTest]
        public IEnumerator LanguageCanChangeAtRuntime()
        {
            Localizer.SetLanguage(GameLanguage.English);
            Assert.That(Localizer.Get("menu.play"), Is.EqualTo("PLAY"));
            Localizer.SetLanguage(GameLanguage.Turkish);
            Assert.That(Localizer.Get("menu.play"), Is.EqualTo("OYNA"));
            yield return null;
        }
    }
}
