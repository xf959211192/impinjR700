using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImpinjR700
{
    public enum EpcCharacterOutputMode
    {
        Single = 0,
        Continuous = 1
    }

    public readonly record struct EpcOutputCharacter(string Value, string DisplayName, bool IsBuiltIn);

    public sealed class EpcCharacterOutputSettings
    {
        private const double DefaultDebounceSeconds = 0.5;
        public const string EscapeActionValue = "{ESC}";
        public const string TabActionValue = "{TAB}";
        public const string CapsActionValue = "{CAPS}";
        public const string EnterActionValue = "{ENTER}";
        public const string ShiftActionValue = "{SHIFT}";
        public const string DeleteActionValue = "{DELETE}";

        public static readonly EpcOutputCharacter[] AllowedCharacters =
        {
            new(EscapeActionValue, "Esc", true),
            new("1", "1", true),
            new("2", "2", true),
            new("3", "3", true),
            new("4", "4", true),
            new("5", "5", true),
            new("6", "6", true),
            new("7", "7", true),
            new("8", "8", true),
            new("9", "9", true),
            new("0", "0", true),
            new("-", "-", true),
            new("=", "=", true),
            new(DeleteActionValue, "删除", true),
            new(TabActionValue, "Tab", true),
            new("Q", "Q", true),
            new("W", "W", true),
            new("E", "E", true),
            new("R", "R", true),
            new("T", "T", true),
            new("Y", "Y", true),
            new("U", "U", true),
            new("I", "I", true),
            new("O", "O", true),
            new("P", "P", true),
            new("[", "[", true),
            new("]", "]", true),
            new(CapsActionValue, "Caps", true),
            new("A", "A", true),
            new("S", "S", true),
            new("D", "D", true),
            new("F", "F", true),
            new("G", "G", true),
            new("H", "H", true),
            new("J", "J", true),
            new("K", "K", true),
            new("L", "L", true),
            new(";", ";", true),
            new("'", "'", true),
            new(EnterActionValue, "Enter", true),
            new(ShiftActionValue, "Shift", true),
            new("Z", "Z", true),
            new("X", "X", true),
            new("C", "C", true),
            new("V", "V", true),
            new("B", "B", true),
            new("N", "N", true),
            new("M", "M", true),
            new(",", ",", true),
            new(".", ".", true),
            new("/", "/", true),
            new(" ", "空格", true)
        };

        public Dictionary<string, string> BindingsByCharacter { get; set; } = new(StringComparer.Ordinal);

        public List<string> CustomCharacters { get; set; } = new();

        public EpcCharacterOutputMode Mode { get; set; } = EpcCharacterOutputMode.Single;

        public double DebounceSeconds { get; set; } = DefaultDebounceSeconds;

        public EpcCharacterOutputSettings Clone()
        {
            return new EpcCharacterOutputSettings
            {
                Mode = Mode,
                DebounceSeconds = DebounceSeconds,
                BindingsByCharacter = new Dictionary<string, string>(BindingsByCharacter ?? new Dictionary<string, string>(), StringComparer.Ordinal),
                CustomCharacters = new List<string>(CustomCharacters ?? new List<string>())
            };
        }

        public static bool IsAllowedCharacter(string value)
        {
            return AllowedCharacters.Any(item => string.Equals(item.Value, value, StringComparison.Ordinal));
        }

        public bool IsAvailableCharacter(string value)
        {
            return IsAllowedCharacter(value) || CustomCharacters.Any(item => string.Equals(item, value, StringComparison.Ordinal));
        }

        public static string GetCharacterDisplayName(string value)
        {
            return AllowedCharacters.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.Ordinal)).DisplayName
                ?? value;
        }

        public IReadOnlyList<EpcOutputCharacter> GetAvailableCharacters()
        {
            return AllowedCharacters
                .Concat(CustomCharacters.Select(value => new EpcOutputCharacter(value, value, false)))
                .ToArray();
        }
    }

    public sealed class EpcCharacterOutputEngine
    {
        private readonly Dictionary<string, string> _characterByEpc = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _lastEmitTimeByEpc = new(StringComparer.Ordinal);
        private EpcCharacterOutputMode _mode = EpcCharacterOutputMode.Single;
        private TimeSpan _debounceInterval = TimeSpan.FromSeconds(0.5);

        public string CurrentOutput { get; private set; } = string.Empty;

        public void UpdateSettings(EpcCharacterOutputSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            _mode = settings.Mode;
            _debounceInterval = TimeSpan.FromSeconds(Math.Max(0, settings.DebounceSeconds));
            _characterByEpc.Clear();
            var availableCharacters = settings.GetAvailableCharacters()
                .Select(character => character.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var binding in settings.BindingsByCharacter)
            {
                if (!availableCharacters.Contains(binding.Key))
                {
                    continue;
                }

                var epc = NormalizeEpc(binding.Value);
                if (string.IsNullOrEmpty(epc))
                {
                    continue;
                }

                _characterByEpc[epc] = binding.Key;
            }
        }

        public bool TryEmit(string epc, DateTime timestamp, out string emittedCharacter, out string currentOutput)
        {
            emittedCharacter = string.Empty;
            currentOutput = CurrentOutput;

            var normalizedEpc = NormalizeEpc(epc);
            if (string.IsNullOrEmpty(normalizedEpc) || !_characterByEpc.TryGetValue(normalizedEpc, out var character))
            {
                return false;
            }

            if (_lastEmitTimeByEpc.TryGetValue(normalizedEpc, out var lastEmitTime) &&
                timestamp - lastEmitTime < _debounceInterval)
            {
                return false;
            }

            _lastEmitTimeByEpc[normalizedEpc] = timestamp;
            emittedCharacter = character;
            CurrentOutput = ApplyOutputCharacter(character);
            currentOutput = CurrentOutput;
            return true;
        }

        private string ApplyOutputCharacter(string character)
        {
            if (string.Equals(character, EpcCharacterOutputSettings.DeleteActionValue, StringComparison.Ordinal))
            {
                return CurrentOutput.Length == 0
                    ? string.Empty
                    : CurrentOutput[..^1];
            }

            if (string.Equals(character, EpcCharacterOutputSettings.EnterActionValue, StringComparison.Ordinal))
            {
                return _mode == EpcCharacterOutputMode.Single ? Environment.NewLine : CurrentOutput + Environment.NewLine;
            }

            if (string.Equals(character, EpcCharacterOutputSettings.TabActionValue, StringComparison.Ordinal))
            {
                return _mode == EpcCharacterOutputMode.Single ? "\t" : CurrentOutput + "\t";
            }

            if (string.Equals(character, EpcCharacterOutputSettings.EscapeActionValue, StringComparison.Ordinal) ||
                string.Equals(character, EpcCharacterOutputSettings.CapsActionValue, StringComparison.Ordinal) ||
                string.Equals(character, EpcCharacterOutputSettings.ShiftActionValue, StringComparison.Ordinal))
            {
                return CurrentOutput;
            }

            return _mode == EpcCharacterOutputMode.Single
                ? character
                : CurrentOutput + character;
        }

        public string ClearOutput()
        {
            CurrentOutput = string.Empty;
            return CurrentOutput;
        }

        public static string NormalizeEpc(string? epc)
        {
            return epc?.Trim() ?? string.Empty;
        }
    }

    public sealed class EpcCharacterOutputSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string DefaultFilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImpinjR700",
            "epc-character-output.json");

        private readonly string _filePath;

        public EpcCharacterOutputSettingsStore()
            : this(DefaultFilePath)
        {
        }

        public EpcCharacterOutputSettingsStore(string filePath)
        {
            _filePath = filePath;
        }

        public EpcCharacterOutputSettings Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return CreateDefaultSettings();
                }

                var content = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<EpcCharacterOutputSettings>(content, JsonOptions)
                    ?? CreateDefaultSettings();
                return Sanitize(settings);
            }
            catch
            {
                return CreateDefaultSettings();
            }
        }

        public void Save(EpcCharacterOutputSettings settings)
        {
            var safeSettings = Sanitize(settings);
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(safeSettings, JsonOptions));
        }

        public static EpcCharacterOutputSettings CreateDefaultSettings()
        {
            return new EpcCharacterOutputSettings
            {
                Mode = EpcCharacterOutputMode.Single,
                DebounceSeconds = 0.5,
                BindingsByCharacter = new Dictionary<string, string>(StringComparer.Ordinal),
                CustomCharacters = new List<string>()
            };
        }

        private static EpcCharacterOutputSettings Sanitize(EpcCharacterOutputSettings settings)
        {
            var safeSettings = settings.Clone();
            safeSettings.DebounceSeconds = Math.Max(0, safeSettings.DebounceSeconds);
            safeSettings.CustomCharacters = SanitizeCustomCharacters(safeSettings.CustomCharacters);

            var safeBindings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var character in safeSettings.GetAvailableCharacters())
            {
                if (!safeSettings.BindingsByCharacter.TryGetValue(character.Value, out var epc))
                {
                    continue;
                }

                epc = EpcCharacterOutputEngine.NormalizeEpc(epc);
                if (!string.IsNullOrEmpty(epc))
                {
                    safeBindings[character.Value] = epc;
                }
            }

            safeSettings.BindingsByCharacter = safeBindings;
            return safeSettings;
        }

        private static List<string> SanitizeCustomCharacters(IEnumerable<string>? customCharacters)
        {
            var safeCharacters = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in customCharacters ?? Enumerable.Empty<string>())
            {
                var character = value.Trim();
                if (string.IsNullOrEmpty(character) ||
                    IsReservedCharacterText(character) ||
                    !seen.Add(character))
                {
                    continue;
                }

                safeCharacters.Add(character);
            }

            return safeCharacters;
        }

        private static bool IsReservedCharacterText(string value)
        {
            return EpcCharacterOutputSettings.AllowedCharacters.Any(character =>
                string.Equals(character.Value, value, StringComparison.Ordinal) ||
                string.Equals(character.DisplayName, value, StringComparison.Ordinal));
        }
    }
}
