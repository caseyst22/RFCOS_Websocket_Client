using System.Net.Mime;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Client
{
    static class AccessCodes
    {
        public const string dev = "GEADEV";
        public const string qa = "GEAQA";
        public const string prod = "GEAPROD";

    }

    public class TagBlinkReader
    {
        const int RECEIVED_BUFFER_SIZE = 64000;
        const string ACCESS_CODE = AccessCodes.dev;
        const string TAG_ID_MASK = "3314052B4C000042";

        private string ProcessJson(string json)
        {
            JArray jarray = JArray.Parse(json);
            
            foreach (var tag in jarray.ToList())
            {
                //Tag Mask
                string? tagId = (string?)tag["tagID"];
                if (tagId == null || !tagId.StartsWith(TAG_ID_MASK))
                {
                    jarray.Remove(tag);
                    Console.WriteLine("Removed tag " + tagId);
                    continue;
                }

                //Add AccessCode property
                tag["AccessCode"] = ACCESS_CODE;
            }

            Console.WriteLine("Remaining Tags:");
            foreach (var tag in jarray) Console.WriteLine(tag["tagID"] + " " + tag["AccessCode"]);
            Console.WriteLine();

            return jarray.ToString();
        }

        private string InsertAccessCodes(string tags, string code)
        {
            string body = tags;
            string insertString = $"\"AccessCode\":\"{code}\",";
            int i = 0;
            while (i < body.Length)
            {
                if (body[i++] == '{')
                {
                    body = body.Insert(i, insertString);
                    i += insertString.Length;
                }
            }
            return body;
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

                ClientWebSocket os_sock = new ClientWebSocket();

                //Set header
                string encStr = UserName + ":" + Password;
                byte[] encBytes = Encoding.UTF8.GetBytes(encStr);
                encStr = Convert.ToBase64String(encBytes);
                os_sock.Options.SetRequestHeader("Authorization", "Basic " + encStr);

                Uri uri = new Uri(url_s);

                await os_sock.ConnectAsync(uri, CancellationToken.None);

                /* Stream is open */
                if (os_sock.State == WebSocketState.Open)
                {
                    byte[] rBytes = new byte[RECEIVED_BUFFER_SIZE];
                    ArraySegment<byte> rSeg = new ArraySegment<byte>(rBytes);

                    Console.WriteLine("Start Connection");

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
                    Console.WriteLine("Subscribed\n");

                    //Create Http Client
                    HttpClient client = new HttpClient()
                    {
                        BaseAddress = new Uri("https://geaqaapi.wavereaction.com"),
                    };

                    while (true)
                    {
                        int numberBytesReceived = 0;
                        WebSocketReceiveResult ws;

                        ws = await os_sock.ReceiveAsync(new ArraySegment<byte>(rBytes, numberBytesReceived, (RECEIVED_BUFFER_SIZE - numberBytesReceived)), CancellationToken.None);
                        numberBytesReceived = ws.Count;

                        //STOMP packets can extend multiple buffers, keep checking and reading until entire package is done
                        while ((ws.EndOfMessage == false) && numberBytesReceived < RECEIVED_BUFFER_SIZE)
                        {
                            ws = await os_sock.ReceiveAsync(new ArraySegment<byte>(rBytes, numberBytesReceived, (RECEIVED_BUFFER_SIZE - numberBytesReceived)), CancellationToken.None);
                            numberBytesReceived += ws.Count;
                        }

                        string info  = Encoding.Default.GetString(rBytes); //convert to string

                        //Parse STOMP message
                        string[] headers = info.Split('\n'); //Initial header fields of stomp protocol use line feeds to separate fields. Body of message is after a blank line feed
                        string tags = headers[headers.GetUpperBound(0)]; //Body is last index of headers

                        //Construct Json body
                        string body = ProcessJson(tags);
                        Console.WriteLine(body + "\n");

                        // Post to Wave endpoint
                        using StringContent jsonContent = new StringContent(
                            body,
                            Encoding.UTF8,
                            MediaTypeNames.Application.Json
                        );
                        using HttpResponseMessage response = await client.PostAsync("v6/rfid/rfcontrol/tagsave", jsonContent);

                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"{response.StatusCode}: {jsonResponse}\n");

                        //Send out byte to keep alive
                        string contStr = "\n";
                        await os_sock.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(contStr)), WebSocketMessageType.Text, true, CancellationToken.None);
                    }

                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}