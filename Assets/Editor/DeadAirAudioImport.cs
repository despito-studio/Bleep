using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    // Import policy applies only to the separately produced, file-backed game library.
    public sealed class DeadAirAudioImport : AssetPostprocessor
    {
        public const string Root = "Assets/Resources/Audio/";

        public override uint GetVersion() => 1;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root, StringComparison.Ordinal)) return;
            Configure((AudioImporter)assetImporter, assetPath);
        }

        public static void Configure(AudioImporter importer, string path)
        {
            var policy = Policy(path, WavSeconds(path));
            var settings = importer.defaultSampleSettings;
            settings.loadType = policy.loadType;
            settings.compressionFormat = policy.compression;
            settings.quality = policy.quality;
            settings.preloadAudioData = policy.preload;
            // The 160 BPM score uses exactly 18,000 frames per beat at 48 kHz.
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.SetOverrideSampleSettings("Standalone", settings);
            importer.forceToMono = policy.mono;
            importer.loadInBackground = policy.background;
            importer.ambisonic = false;
        }

        public static ImportPolicy Policy(string path, double seconds)
        {
            bool music = path.StartsWith(Root + "Music/", StringComparison.Ordinal);
            bool rooms = path.StartsWith(Root + "Cues/Rooms/", StringComparison.Ordinal);
            bool beds = path.StartsWith(Root + "Cues/Beds/", StringComparison.Ordinal);
            bool falls = path.StartsWith(Root + "Cues/Falls/", StringComparison.Ordinal);
            bool effects = path.StartsWith(Root + "Cues/Effects/", StringComparison.Ordinal);
            bool generator = Path.GetFileNameWithoutExtension(path) == "generator-bed";
            bool streaming = music || rooms || beds && seconds > 5;
            bool medium = falls || !streaming && seconds > 3;
            return new ImportPolicy
            {
                mono = effects || falls || generator,
                loadType = streaming ? AudioClipLoadType.Streaming : medium ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad,
                compression = streaming || medium ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM,
                quality = streaming ? .75f : medium ? .8f : 1,
                background = streaming,
                preload = !streaming
            };
        }

        // Read the source WAV header without loading/decompressing the whole recording.
        public static double WavSeconds(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Audio source missing", path);
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected RIFF WAV: " + path);
                reader.ReadUInt32();
                if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAV: " + path);
                uint byteRate = 0, dataBytes = 0;
                while (stream.Position + 8 <= stream.Length)
                {
                    string chunk = new string(reader.ReadChars(4));
                    uint bytes = reader.ReadUInt32();
                    long next = stream.Position + bytes + (bytes & 1);
                    if (next > stream.Length) throw new InvalidDataException("Truncated WAV: " + path);
                    if (chunk == "fmt " && bytes >= 16)
                    {
                        reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt32();
                        byteRate = reader.ReadUInt32();
                    }
                    else if (chunk == "data") dataBytes = bytes;
                    stream.Position = next;
                }
                if (byteRate == 0 || dataBytes == 0) throw new InvalidDataException("Empty WAV: " + path);
                return dataBytes / (double)byteRate;
            }
        }

        public static int WavChannels(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 12;
                while (stream.Position + 8 <= stream.Length)
                {
                    string chunk = new string(reader.ReadChars(4));
                    uint bytes = reader.ReadUInt32();
                    long next = stream.Position + bytes + (bytes & 1);
                    if (next > stream.Length) throw new InvalidDataException("Truncated WAV: " + path);
                    if (chunk == "fmt " && bytes >= 16) { reader.ReadUInt16(); return reader.ReadUInt16(); }
                    stream.Position = next;
                }
            }
            throw new InvalidDataException("Missing channel header: " + path);
        }

        [MenuItem("Dead Air/Audio/Apply Imported Audio Policy")]
        public static void ApplyToLibrary()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in Directory.GetFiles(Root, "*.wav", SearchOption.AllDirectories))
            {
                string normalized = path.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(normalized) as AudioImporter;
                if (importer == null) throw new InvalidOperationException("AudioImporter unavailable: " + normalized);
                Configure(importer, normalized);
                importer.SaveAndReimport();
            }
            Debug.Log("DEAD AIR AUDIO IMPORT: file-backed library configured and reimported through Unity AudioImporter.");
        }

        public struct ImportPolicy
        {
            public AudioClipLoadType loadType;
            public AudioCompressionFormat compression;
            public float quality;
            public bool mono, background, preload;
        }
    }
}
