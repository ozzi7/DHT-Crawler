using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using BencodeLibrary;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Collections;

namespace DHTCrawler
{
    /// <summary>
    /// A node in the DHT network
    /// </summary>
    class DHTNode
    {
        private string nodeIDHex;
        private int localPort;
        private List<string> linfoHHex;  
        private Thread receiveThread;
        private UdpClient udpClient;
        private int conversationIDNr = -1;
        private List<HashSet<Tuple<string, IPEndPoint>>> lContactNodes;
        private List<HashSet<Tuple<string, IPEndPoint>>> lContactNodesAsked;
        private List<List<IPEndPoint>> lTorrentPeers;
        private List<int> bucketIndices;
        private IPEndPoint receiveFrom;
        private const int SIO_UDP_CONNRESET = -1744830452;
        private List<string> lockContactNodes = Enumerable.Repeat("c", Program.MAX_INFOHASHES_PER_NODE).ToList();
        private string lockTorrentPeers = "p";
        private int countRecvPeerPackets = 0;
        private int countRecvNodePackets = 0;
        private int countGetPeerReqSent = 0;
        private static readonly Random random = new Random();
        private MySQL mySQL = new MySQL();

        public DHTNode(int localPort)
        {
            this.localPort = localPort;
            nodeIDHex = getRandomID();
            initInfoHashes();
            lContactNodes = new List<HashSet<Tuple<string, IPEndPoint>>>();
            lContactNodesAsked = new List<HashSet<Tuple<string, IPEndPoint>>>();
            lTorrentPeers = new List<List<IPEndPoint>>();
            bucketIndices = new List<int>();

            udpClient = new UdpClient(localPort);
            udpClient.Client.ReceiveBufferSize = 10000; // in bytes
            udpClient.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);

            receiveThread = new Thread(new ThreadStart(ReceivePackets));
            receiveThread.IsBackground = true;
            receiveThread.Start();

            // 1. Send get_peers request to 100 closest nodes for each infohash
            for (int i = 0; i < Program.MAX_INFOHASHES_PER_NODE; i++)
            {
                string conversationID = getNextConversatioID();
                int index = getIndexFromConvID(conversationID);
                bucketIndices.Add(getBucketIndexFromInfohash(linfoHHex[index]));
                foreach (Tuple<string, IPEndPoint> t in Program.dhtBucketM.lDHTNodes[bucketIndices[index]])
                {
                    GetPeers(t.Item2, linfoHHex[index],conversationID);
                    countGetPeerReqSent++;
                    Wait(1);
                }
            }

            // Wait for all responses to be processed
            Wait(10000);

            // ask all from corresponding bucket
            // depending on how many seeders/leechers ask bucket left and right (wrap around)
            // 2. ask all which were returned as closer nodes, loop 2 until everyone asked
            //Log("Contact nodes: " + lContactNodes.Count);
            //Log("Peers found: " + lTorrentPeers.Count);
            //Log("Peer requests sent: " + countGetPeerReqSent);
            //Log("Received peer packets: " + countRecvPeerPackets);
            //Log("Received node packets: " + countRecvNodePackets);
        }
        private int getIndexFromConvID(string conversationID)
        {
            byte[] b = System.Text.Encoding.ASCII.GetBytes(conversationID);
            return BitConverter.ToInt32(b, 0);
        }
        private int getBucketIndexFromInfohash(string infohash)
        {
            byte[] b = System.Text.Encoding.ASCII.GetBytes(infohash);
            byte[] b2 = new byte[4]{0,0,0,0};
            Array.Copy(b, 0, b2, 0, 2);
            return BitConverter.ToInt32(b2, 0);
        }
        private void GetPeers(IPEndPoint toAdr, string infohash, string conversationID)
        {
            string getPeersMsg = "d1:ad2:id20:" + HexSToString(nodeIDHex) + "9:info_hash20:" + HexSToString(infohash) + "e1:q9:get_peers1:t2:" + conversationID + "1:y1:qe";
            Log("Sent GetPeers request to " + toAdr);
            SendMessage(toAdr, getPeersMsg);
        }
        private void FindNode(IPEndPoint toAdr, string findNodeID, string conversationID)
        {
            string findNodeMsg = "d1:ad2:id20:" + HexSToString(nodeIDHex) + "6:target20:" + HexSToString(findNodeID) + "e1:q9:find_node1:t2:" + conversationID + "1:y1:qe";
            Log("Sent FindNode request to " + toAdr);
            SendMessage(toAdr, findNodeMsg);
        }
        private void SendMessage(IPEndPoint destination, string message)
        {
            byte[] send_buffer = BencodingUtils.ExtendedASCIIEncoding.GetBytes(message);
            udpClient.Send(send_buffer, send_buffer.Length, destination);
        }
        /// <summary>
        /// Not thread safe, call when all threads have finished
        /// </summary>
        private void OutputContactList()
        {
            Log("Contact list:");
            for (int index = 0; index < lContactNodes.Count; index++)
            {
                //Log(index.ToString() + ": " + lContactNodes.ElementAt(index).Item1 + " " + lContactNodes.ElementAt(index).Item2.ToString());
            }
        }
        private void OutputTorrentPeerList()
        {
            Log("Torrent peer list:");
            for (int i = 0; i < lTorrentPeers.Count; i++)
            {
                Log(i.ToString() + ": " + lTorrentPeers[i].ToString());
            }
        }
        private void Log(string message)
        {
            //if (Program.debug)
            //    Console.WriteLine(message);
        }
        /// <summary>
        /// Updates the list of contact nodes with closer nodes
        /// </summary>
        /// <param name="nodeIpPortString"></param>
        private void UpdateContactList(BString nodeIpPortString, BString transactionID)
        {
            byte[] arr = BencodingUtils.ExtendedASCIIEncoding.GetBytes(nodeIpPortString.Value);
            byte[] arr2 = BencodingUtils.ExtendedASCIIEncoding.GetBytes(transactionID.Value);
            byte[] ip = new byte[4];
            byte[] port = new byte[2];
            byte[] nodeID = new byte[20];
            for (int i = 0; i < arr.Length / 26; i++)
            {
                byte[] transID = new byte[4] { 0, 0, 0, 0 };
                Array.Copy(arr, i * 26, nodeID, 0, 20);
                Array.Copy(arr, i * 26 + 20, ip, 0, 4);
                Array.Copy(arr, i * 26 + 24, port, 0, 2);
                Array.Copy(arr2, 0, transID, 2, 2);
                Array.Reverse(port);
                IPEndPoint ipEndP = new IPEndPoint((Int64)BitConverter.ToUInt32(ip, 0), (Int32)BitConverter.ToUInt16(port, 0));
                string sNodeID = ByteArrayToHexString(nodeID);

                lock (lockContactNodes)
                {
                    if (!lContactNodes[BitConverter.ToInt32(transID, 0)].Contains(Tuple.Create(sNodeID, ipEndP)))
                    {
                        lContactNodes[BitConverter.ToInt32(transID, 0)].Add(Tuple.Create(sNodeID, ipEndP));
                    }
                }
            }
        }
        private void UpdateTorrentPeerList(BList lIPPortString, BString transactionID)
        {
            byte[] ip = new byte[4];
            byte[] port = new byte[2];
            byte[] arr2 = BencodingUtils.ExtendedASCIIEncoding.GetBytes(transactionID.Value);

            for (int k = 0; k < lIPPortString.Count; k++)
            {
                byte[] transID = new byte[4] { 0, 0, 0, 0 };
                Array.Copy(arr2, 0, transID, 2, 2);
                BString tempBS = (BString)lIPPortString[k];
                byte[] arr = BencodingUtils.ExtendedASCIIEncoding.GetBytes(tempBS.Value);
                Array.Copy(arr, 0, ip, 0, 4);
                Array.Copy(arr, 4, port, 0, 2);
                Array.Copy(arr2, 0, transID, 2, 2);
                Array.Reverse(port);
                IPEndPoint ipEndP = new IPEndPoint((Int64)BitConverter.ToUInt32(ip, 0), (Int32)BitConverter.ToUInt16(port, 0));
                lock (lockTorrentPeers)
                {
                    if (!lTorrentPeers[BitConverter.ToInt32(transID, 0)].Contains(ipEndP))
                    {
                        lTorrentPeers[BitConverter.ToInt32(transID, 0)].Add(ipEndP);
                    }
                }
            }
        }
        /// <summary>
        /// Converts nodeID from BString to displayable hex string
        /// </summary>
        /// <param name="nodeID"></param>
        /// <returns></returns>
        private string BStringToHexNodeID(BString nodeID)
        {
            string hex = BitConverter.ToString(BencodingUtils.ExtendedASCIIEncoding.GetBytes(nodeID.Value));
            return hex.Replace("-", "");
        }
        /// <summary>
        /// Converts nodeID from byte array to displayable hex string
        /// </summary>
        /// <param name="arr"></param>
        /// <returns></returns>
        private string ByteArrayToHexString(byte[] arr)
        {
            string hex = BitConverter.ToString(arr);
            return hex.Replace("-", "");
        }
        /// <summary>
        /// Convert string in hex to string - assumes length of string is 2n
        /// </summary>
        /// <param name="hexNodeID"></param>
        /// <returns></returns>
        private string HexSToString(string hexString)
        {
            int length = hexString.Length / 2;
            byte[] arr = new byte[length];
            for (int i = 0; i < length; i++)
            {
                arr[i] = Convert.ToByte(hexString.Substring(2 * i, 2), 16);
            }
            return BencodingUtils.ExtendedASCIIEncoding.GetString(arr);
        }
        /// <summary>
        /// Wait for specified ms
        /// </summary>
        /// <param name="ms"></param>
        private void Wait(int ms)
        {
            DateTime called = DateTime.Now;
            while (DateTime.Now < called.Add(new TimeSpan(ms * 10000))) { }
        }
        /// <summary>
        /// Wait for Program.MAX_SYNC_WAIT
        /// </summary>
        private void Wait()
        {
            DateTime called = DateTime.Now;
            while (DateTime.Now < called.Add(Program.MAX_SYNC_WAIT)) { }
        }
        private string getRandomID()
        {
            byte[] b = new byte[20];
            random.NextBytes(b);
            return ByteArrayToHexString(b);
        }
        private string getRandomConvID()
        {
            byte[] b = new byte[2];
            random.NextBytes(b);
            return ByteArrayToHexString(b);
        }
        IEnumerable<bool> GetBits(byte b)
        {
            for (int i = 0; i < 8; i++)
            {
                yield return (b & 0x80) != 0;
                b *= 2;
            }
        }
        private string getNextConversatioID()
        {
            conversationIDNr++;
            byte[] b = new byte[2];
            b[1] = (byte)(conversationIDNr & 0xFF);
            b[0] = (byte)((conversationIDNr >> 8) & 0xFF);
            return ByteArrayToHexString(b);
        }
        private void initInfoHashes()
        {
            for (int i = 0; i < Program.MAX_INFOHASHES_PER_NODE; i++)
            {
                string infoHHex;
                infoHHex = mySQL.getNextInfoHash();
                linfoHHex.Add(infoHHex);
            }
        }
        /// <summary>
        /// Background thread handling all incoming traffic
        /// </summary>
        private void ReceivePackets()
        {
            while (true)
            {
                try
                {
                    // Get a datagram
                    receiveFrom = new IPEndPoint(IPAddress.Any, localPort);
                    byte[] data = udpClient.Receive(ref receiveFrom);

                    // Decode the message
                    Stream stream = new MemoryStream(data);
                    IBencodingType receivedMsg = BencodingUtils.Decode(stream);
                    string decoded = BencodingUtils.ExtendedASCIIEncoding.GetString(data.ToArray());
                    Log("Received message!");
                    //Log(decoded);

                    // t is transaction id
                    // y is e for error, r for reply, q for query
                    if (receivedMsg is BDict) // throws error.. todo: fix
                    {
                        BDict dictMsg = (BDict)receivedMsg;
                        if (dictMsg.ContainsKey("y"))
                        {
                            if (dictMsg["y"].Equals(new BString("e")))
                            {
                                // received error
                                Log("Received error! (ignored)");
                            }
                            else if (dictMsg["y"].Equals(new BString("r")))
                            {
                                // received reply
                                if (dictMsg.ContainsKey("r"))
                                {
                                    if (dictMsg["r"] is BDict)
                                    {
                                        BDict dictMsg2 = (BDict)dictMsg["r"];
                                        if (dictMsg2.ContainsKey("values") && dictMsg.ContainsKey("t"))
                                        {
                                            Log("Received list of peers for torrent!");
                                            countRecvPeerPackets++;
                                            BList peerAdrs = (BList)dictMsg2["values"];
                                            UpdateTorrentPeerList(peerAdrs, (BString)dictMsg["t"]);
                                        }
                                        else if (dictMsg2.ContainsKey("nodes") && dictMsg.ContainsKey("t"))
                                        {
                                            // could be an answer to find node or get peers
                                            Log("Received list of nodeID & IP & port!");
                                            countRecvNodePackets++;
                                            BString nodeIDString = (BString)dictMsg2["nodes"];
                                            UpdateContactList(nodeIDString, (BString)dictMsg["t"]);
                                        }
                                        else
                                        {
                                        }
                                    }
                                    else
                                    {
                                    }
                                }
                            }
                            else if (dictMsg["y"].Equals(new BString("q")))
                            {
                                // received query
                                Log("Received query! (ignored)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Error receiving data: " + ex.ToString());
                }
            }
        }
    }
}