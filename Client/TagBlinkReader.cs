using System.Net.WebSockets;
using System.Net.Mime;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Client
{

    public class TagBlinkReader
    {
        const int RECEIVED_BUFFER_SIZE = 64000;
        string AccessCode { get; set;}
        List<string> Masks { get; set; }
        StreamWriter logWriter { get; set; }

        public TagBlinkReader(bool log, string accessCode, List<string> masks)
        {
            this.AccessCode = accessCode;
            this.Masks = masks;

            if (log) 
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), $"logfile_{DateTime.Now.Date}");
                logWriter = new StreamWriter(path);
            }
            else
            {
                logWriter = StreamWriter.Null;
            }
        }

        private void Print(string message)
        {
            Console.Write(message);
            logWriter.Write($"{DateTime.Now}\t{message}");
        }

        protected string ProcessJson(string json)
        {
            JArray jarray = JArray.Parse(json);
            
            foreach (var tag in jarray.ToList())
            {
                //Tag Mask
                string? tagId = (string?)tag["tagID"];
                if (tagId == null || !Masks.Any(tagId.StartsWith))
                {
                    jarray.Remove(tag);
                    Print($"Removed tag {tagId}\n");
                    continue;
                }

                //Add AccessCode property
                tag["AccessCode"] = AccessCode;
            }

            Print("\n");

            return jarray.ToString();
        }

        public async Task ReadRFServAsync()
        {
            try
            {
                /* Establishing Stream */

                string url_s = "ws://127.0.0.1:8888/websockets/messaging/websocket"; //Websocket url

                //Admin username and password needed in header
                string UserName = "admin"; 
                string Password = "admin";

                //Create Header
                string encStr = UserName + ":" + Password;
                byte[] encBytes = Encoding.UTF8.GetBytes(encStr);
                encStr = Convert.ToBase64String(encBytes);

                //Create uri
                Uri uri = new Uri(url_s);

                ClientWebSocket os_sock = new ClientWebSocket();

                //Wait for connection
                int attempt = 0, maxAttempts = 30;
                while (os_sock.State != WebSocketState.Open)
                {
                    //Exit if maxAttempts is reached
                    if (attempt++ == maxAttempts)
                    {
                        throw new Exception("Unable to establish stream");
                    }

                    Print($"Attempting to establish stream: Attempt {attempt}\n");
                    try
                    {
                        os_sock.Options.SetRequestHeader("Authorization", "Basic " + encStr);
                        await os_sock.ConnectAsync(uri, CancellationToken.None);
                    }
                    catch (Exception e) 
                    {
                        Print($"{e.Message}\n\n");
                        await Task.Delay(30000); //Wait for 30 seconds before retrying
                        os_sock = new ClientWebSocket();
                    }
                }

                /* Stream is open */
                if (os_sock.State == WebSocketState.Open)
                {
                    byte[] rBytes = new byte[RECEIVED_BUFFER_SIZE];
                    ArraySegment<byte> rSeg = new ArraySegment<byte>(rBytes);

                    Print("Start Connection\n");

                    //First message must be a CONNECT frame
                    //Header fields give version and heartbeat (heartbeat tests healthiness of TCP connection)
                    string startStr = "CONNECT\naccept-version:1.1,1.0\nheart-beat:10000,10000\n\n\u0000";
                    await os_sock.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(startStr)), WebSocketMessageType.Text, true, CancellationToken.None);

                    await os_sock.ReceiveAsync(rSeg, CancellationToken.None);

                    //SUBSCRIBE frame is used to register to listen to a given destination
                    //SUBSCRIBE frames require an id due to multithreading
                    int idnum = 0; 
                    string idNumS = idnum.ToString();

                    string subStr = "SUBSCRIBE\nid: " + idNumS + "\ndestination:/topic/tagBlinkLite.*\nack:auto\n\n\u0000";
                    await os_sock.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(subStr)), WebSocketMessageType.Text, true, CancellationToken.None);

                    await os_sock.ReceiveAsync(rSeg, CancellationToken.None);
                    Print("Subscribed\n\n");

                    //Create Http Client
                    HttpClient client = new HttpClient()
                    {
                        BaseAddress = new Uri("https://geaqaapi.wavereaction.com"),
                    };

                    while (true)
                    {
                        try
                        {
                            StringBuilder messageBuilder = new StringBuilder();
                            WebSocketReceiveResult ws;

                            do
                            {
                                ws = await os_sock.ReceiveAsync(rSeg, CancellationToken.None);
                                messageBuilder.Append(Encoding.UTF8.GetString(rBytes, 0, ws.Count));
                            }
                            while (!ws.EndOfMessage);

                            string info = messageBuilder.ToString();

                            // int numberBytesReceived = 0;
                            // WebSocketReceiveResult ws;

                            // ws = await os_sock.ReceiveAsync(new ArraySegment<byte>(rBytes, numberBytesReceived, (RECEIVED_BUFFER_SIZE - numberBytesReceived)), CancellationToken.None);
                            // numberBytesReceived = ws.Count;

                            // //STOMP packets can extend multiple buffers, keep checking and reading until entire package is done
                            // while ((ws.EndOfMessage == false) && numberBytesReceived < RECEIVED_BUFFER_SIZE)
                            // {
                            //     ws = await os_sock.ReceiveAsync(new ArraySegment<byte>(rBytes, numberBytesReceived, (RECEIVED_BUFFER_SIZE - numberBytesReceived)), CancellationToken.None);
                            //     numberBytesReceived += ws.Count;
                            // }

                            // string info  = Encoding.Default.GetString(rBytes); //convert to string

                            //Parse STOMP message
                            string[] headers = info.Split('\n'); //Initial header fields of stomp protocol use line feeds to separate fields. Body of message is after a blank line feed
                            string tags = headers[headers.GetUpperBound(0)]; //Body is last index of headers

                            //Construct Json body
                            string body = ProcessJson(tags);
                            Print($"{body}\n\n");

                            //Post to Wave endpoint
                            using StringContent jsonContent = new StringContent(
                                body,
                                Encoding.UTF8,
                                MediaTypeNames.Application.Json
                            );
                            using HttpResponseMessage response = await client.PostAsync("v6/rfid/rfcontrol/tagsave", jsonContent);

                            var jsonResponse = await response.Content.ReadAsStringAsync();
                            Print($"{response.StatusCode}: {jsonResponse}\n\n");
                        }
                        catch (Exception e)
                        {
                            Print($"{e.Message}\n\n");
                        }

                        //Send out byte to keep alive
                        string contStr = "\n";
                        await os_sock.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(contStr)), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
            catch (Exception e)
            {
                Print($"{e.Message}\n\n");
            }
            finally
            {
                logWriter.Close();
            }
        }
    }
}