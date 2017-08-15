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

namespace DHTCrawler
{
    class Program
    {
        static public TimeSpan MAX_SYNC_WAIT = new TimeSpan(20000000); // 10000 = 1ms
        static public TimeSpan MAX_WAIT_AFTER_IDLE = new TimeSpan(60000000);
        static public int MAX_PACKETS_PER_SECOND_BUCKET_MANAGER = 4000; // Only sending!
        static public int MAX_INFOHASHES_PER_NODE = 10000; // Must be below 65536 for 2 byte conversation IDs!
        static public int DHT_BUCKETS = 65536;
        static public string DHT_FILE_NAME = "DHT_NODES.dat";
        static public int MAX_CONTACT_NODES = 10;
        static public int MAX_GET_PEERS_BURST = 100;
        static public int WAIT_BETWEEN_NODE_COLLECTION_CYCLES = 120000;
        static public bool debug = true;
        static public DHTBucketManager dhtBucketM;

        static void Main(string[] args)
        {
            IPAddress bootstrapIP = IPAddress.Parse("67.215.246.10"); // router.bittorrent.com:6881
            IPEndPoint bootstrapAdr = new IPEndPoint(bootstrapIP, 6881);
            dhtBucketM= new DHTBucketManager(bootstrapAdr, 3245);

            // temporary
            DHTNode dhtNode1 = new DHTNode(3244);
            Console.Read();
        }
    }
}
