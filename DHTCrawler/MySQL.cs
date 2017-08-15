using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;

namespace DHTCrawler
{
    class MySQL
    {
        private MySqlConnection connection;
        private MySqlCommand command;
        private MySqlDataReader reader;

        public void MySql()
        {
            string sConnection = "SERVER=localhost;" + "DATABASE=bachelor;" + "UID=root;" + "PASSWORD=abc;";

            connection = new MySqlConnection(sConnection);
            command = connection.CreateCommand();
        }
        public string getNextInfoHash()
        {
            // Get oldest torrent
            command.CommandText = "SELECT  `infohash` FROM  `torrents` ORDER BY updated ASC LIMIT 1;";
            connection.Open();

            reader = command.ExecuteReader();
            reader.Read();

            string infoHash = reader.GetValue(0).ToString();
            string id = reader.GetValue(1).ToString();
            
            command.CommandText = "UPDATE `torrents` SET updated = `" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"` WHERE id = `" + id + "`";
            command.ExecuteNonQuery();

            connection.Close();

            return infoHash;
        }
        public void insertPeers()
        {

        }
    }
}
