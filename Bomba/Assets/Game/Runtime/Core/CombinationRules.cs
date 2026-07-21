using System;
using System.Collections.Generic;
using System.Linq;

namespace BriefcaseProtocol.Core
{
    public readonly struct CombinationClue
    {
        public CombinationRuleKind Rule { get; }
        public string DisplayValue { get; }
        public IReadOnlyList<string> Tokens { get; }

        public CombinationClue(CombinationRuleKind rule, string displayValue, IReadOnlyList<string> tokens)
        {
            Rule = rule;
            DisplayValue = displayValue ?? string.Empty;
            Tokens = tokens ?? Array.Empty<string>();
        }
    }

    public readonly struct CombinationResult
    {
        public CombinationClue Clue { get; }
        public int Code { get; }
        public string CodeText => Code.ToString("000");

        public CombinationResult(CombinationClue clue, int code)
        {
            Clue = clue;
            Code = Math.Clamp(code, 0, 999);
        }
    }

    public static class CombinationResolver
    {
        private static readonly string[] ColorNames = { "RED", "BLUE", "YELLOW" };
        private static readonly Dictionary<string, int> ColorDigits = new()
        {
            ["RED"] = 3,
            ["BLUE"] = 1,
            ["YELLOW"] = 7
        };

        public static CombinationResult Generate(CombinationRuleKind kind, int seed)
        {
            var random = new Random(seed);
            return kind == CombinationRuleKind.ColorTag ? GenerateColor(random) : GenerateSerial(random);
        }

        public static int Resolve(CombinationClue clue)
        {
            return clue.Rule switch
            {
                CombinationRuleKind.ColorTag => ResolveColors(clue.Tokens),
                CombinationRuleKind.SerialNumber => ResolveSerial(clue.DisplayValue),
                _ => throw new ArgumentOutOfRangeException(nameof(clue))
            };
        }

        private static CombinationResult GenerateColor(Random random)
        {
            var colors = ColorNames.OrderBy(_ => random.Next()).ToArray();
            var clue = new CombinationClue(CombinationRuleKind.ColorTag, string.Join(" - ", colors), colors);
            return new CombinationResult(clue, ResolveColors(colors));
        }

        private static CombinationResult GenerateSerial(Random random)
        {
            var first = random.Next(1, 10);
            var last = random.Next(0, 10);
            var letterA = (char)('A' + random.Next(0, 26));
            var letterB = (char)('A' + random.Next(0, 26));
            var serial = $"{letterA}-{first}{last}-{letterB}2";
            var clue = new CombinationClue(CombinationRuleKind.SerialNumber, serial, Array.Empty<string>());
            return new CombinationResult(clue, ResolveSerial(serial));
        }

        private static int ResolveColors(IReadOnlyList<string> colors)
        {
            if (colors == null || colors.Count != 3)
            {
                throw new ArgumentException("A color clue must contain exactly three tokens.", nameof(colors));
            }

            var code = 0;
            foreach (var color in colors)
            {
                if (!ColorDigits.TryGetValue(color.ToUpperInvariant(), out var digit))
                {
                    throw new ArgumentException($"Unknown color token: {color}", nameof(colors));
                }

                code = code * 10 + digit;
            }

            return code;
        }

        private static int ResolveSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
            {
                throw new ArgumentException("Serial cannot be empty.", nameof(serial));
            }

            var parts = serial.Split('-');
            var digits = parts.Length >= 2 ? parts[1].Where(char.IsDigit).ToArray() : Array.Empty<char>();
            var letterCount = serial.Count(char.IsLetter);
            if (digits.Length < 2 || letterCount > 9)
            {
                throw new ArgumentException($"Invalid serial format: {serial}", nameof(serial));
            }

            return (digits[0] - '0') * 100 + (digits[^1] - '0') * 10 + letterCount;
        }
    }
}
