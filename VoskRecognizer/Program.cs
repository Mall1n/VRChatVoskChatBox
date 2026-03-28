using NAudio.Wave;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Vosk;

namespace VoskRecognizer
{
    internal class Program
    {
        public static string voskModelPath = @"E:\Vosk\Models\vosk-model-ru-0.42";
        public static string voskSmallModelPath = @"E:\Vosk\Models\vosk-model-small-ru-0.22";

        public static string vrchatIp = "127.0.0.1";
        public static int vrchatPort = 9000;

        public static int consoleRow = 0;

        public static DateTime timeLastMessage = new();

        static void Main(string[] args)
        {
            Console.WriteLine("Select Full Vosk model? [Y/n] ([n] Default)");

            string? input = Console.ReadLine() ?? "";

            bool isYes = input.StartsWith("y", StringComparison.OrdinalIgnoreCase);

            string voskModelPath = isYes ? Program.voskModelPath : Program.voskSmallModelPath;

            Model voskModel = new Model(voskModelPath);
            Vosk.VoskRecognizer voskRecognizer = new Vosk.VoskRecognizer(voskModel, 16000);

            WaveInEvent waveIn = new WaveInEvent()
            {
                WaveFormat = new WaveFormat(16000, 1) // Частота 16кГц, 1 канал
            };

            waveIn.StartRecording();

            waveIn.DataAvailable += (sender, e) => WaveIn_DataAvailable(sender, e, voskRecognizer);

            Console.CursorVisible = false;

            Console.WriteLine("Listening...");
            Console.WriteLine();

            consoleRow = Console.CursorTop;

            Console.ReadKey();

            waveIn.StopRecording();
            waveIn.Dispose();
            voskRecognizer.Dispose();
            voskModel.Dispose();
        }

        private static void WaveIn_DataAvailable(object? sender, WaveInEventArgs e, Vosk.VoskRecognizer voskRecognizer)
        {
            if (voskRecognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                string json = voskRecognizer.Result();

                var result = JsonSerializer.Deserialize<VoskResult>(json);

                if (!string.IsNullOrEmpty(result?.text))
                {
                    Console.SetCursorPosition(0, consoleRow);

                    string message = result.text;
                    string output = $"Speach: {message}";

                    Console.WriteLine(output.PadRight((Console.WindowWidth * (Console.CursorTop - consoleRow + 1)) - 1));
                    Console.WriteLine();

                    consoleRow = Console.CursorTop;

                    SendMessageOSC(message);
                }
            }
            else
            {
                string json = voskRecognizer.PartialResult();

                var result = JsonSerializer.Deserialize<VoskResultPartial>(json);

                if (!string.IsNullOrEmpty(result?.partial))
                {
                    Console.SetCursorPosition(0, consoleRow);

                    string message = result.partial;
                    string output = $"Speach: {message}...";

                    Console.Write(output.PadRight(Console.WindowWidth - 1));

                    DateTime dateTime = DateTime.Now; 
                    TimeSpan diff = dateTime - timeLastMessage;
                    if (diff.TotalSeconds > 1d)
                    {
                        SendMessageOSC(result.partial);
                    }
                }
            }
        }

        public static void SendMessageOSC(string message)
        {
            using (var udpClient = new UdpClient())
            {
                byte[] oscData = BuildOscMessage("/chatbox/input", message, true, false);
                udpClient.Send(oscData, oscData.Length, vrchatIp, vrchatPort);
            }

            timeLastMessage = DateTime.Now;
        }

        public static byte[] BuildOscMessage(string address, string message, bool send, bool useInputBox)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteOSCString(writer, address);

                string typeTag = ",s";
                if (send && !useInputBox) typeTag = ",sTF";
                else if (send && useInputBox) typeTag = ",sTT";
                else if (!send && !useInputBox) typeTag = ",sFF";
                else typeTag = ",sFT";

                WriteOSCString(writer, typeTag);

                WriteOSCString(writer, message);

                return stream.ToArray();
            }
        }

        public static void WriteOSCString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes);
            writer.Write((byte)0); // null terminator

            while (writer.BaseStream.Position % 4 != 0)
            {
                writer.Write((byte)0);
            }
        }
    }

    public class VoskResult
    {
        public string text { get; set; }
    }

    public class VoskResultPartial
    {
        public string partial { get; set; }
    }
}
