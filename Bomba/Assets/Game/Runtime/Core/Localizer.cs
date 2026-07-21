using System;
using System.Collections.Generic;

namespace BriefcaseProtocol.Core
{
    public enum GameLanguage : byte
    {
        English,
        Turkish
    }

    public static class Localizer
    {
        private static readonly Dictionary<string, (string English, string Turkish)> Strings = new()
        {
            ["menu.play"] = ("PLAY", "OYNA"),
            ["menu.host"] = ("CREATE PRIVATE LOBBY", "ÖZEL LOBİ OLUŞTUR"),
            ["menu.join"] = ("JOIN BY CODE", "KODLA KATIL"),
            ["lobby.ready"] = ("READY", "HAZIR"),
            ["lobby.waiting"] = ("Waiting for four players", "Dört oyuncu bekleniyor"),
            ["phase.Lobby"] = ("LOBBY", "LOBİ"),
            ["phase.RoleReveal"] = ("ROLE REVEAL", "ROL SUNUMU"),
            ["phase.Setup"] = ("SETUP", "KURULUM"),
            ["phase.Preparation"] = ("PREPARATION", "HAZIRLIK"),
            ["phase.Operation"] = ("OPERATION", "OPERASYON"),
            ["phase.Reveal"] = ("REVEAL", "AÇIKLAMA"),
            ["phase.Results"] = ("RESULTS", "SONUÇLAR"),
            ["interaction.use"] = ("E - Interact", "E - Etkileşim"),
            ["briefcase.wrong"] = ("Wrong code", "Yanlış kod"),
            ["briefcase.open"] = ("Briefcase opened", "Çanta açıldı"),
            ["manual.color.title"] = ("COLOR TAG LOCK", "RENK ETİKETİ KİLİDİ"),
            ["manual.color.body"] = ("Red = 3, Blue = 1, Yellow = 7. Read left to right.", "Kırmızı = 3, Mavi = 1, Sarı = 7. Soldan sağa oku."),
            ["manual.serial.title"] = ("SERIAL NUMBER LOCK", "SERİ NUMARASI KİLİDİ"),
            ["manual.serial.body"] = ("First digit, last digit, then number of letters.", "İlk rakam, son rakam, ardından harf sayısı."),
            ["voice.push"] = ("Hold V to talk", "Konuşmak için V'ye basılı tut"),
            ["error.services"] = ("Online services are unavailable. Local play is still available.", "Çevrimiçi servisler kullanılamıyor. Yerel oyun kullanılabilir.")
        };

        public static GameLanguage Current { get; private set; } = GameLanguage.English;
        public static event Action LanguageChanged;

        public static void SetLanguage(GameLanguage language)
        {
            if (Current == language)
            {
                return;
            }

            Current = language;
            LanguageChanged?.Invoke();
        }

        public static string Get(string key)
        {
            if (!Strings.TryGetValue(key, out var value))
            {
                return $"[{key}]";
            }

            return Current == GameLanguage.Turkish ? value.Turkish : value.English;
        }
    }
}
