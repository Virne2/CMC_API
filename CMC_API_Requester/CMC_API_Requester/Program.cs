using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using static System.Console;
using System.Diagnostics;

namespace CMC_API_Requester
{
    class Program
    {
        static void Main(string[] args)
        {
            //Set the window size appropriately and title the window
            SetWindowSize(160, 40);
            Title = "CoinMarketCap";
            //Instantiate the main class which will be running the bulk of the code. 
            Crypto mCrypto = new Crypto();

            /* This while loop forms the main loop of the program. Every 5 seconds the console will update it's display,
             * showing the requested data in a tablulated format. The data is pulled from coinmarketcap.com, using the basic
             * API requesting tool they have provided. The API Key used is pulled from a file on my computer so as to not store
             * it in plain text in the program, and also to allow multiple API keys to be used. Every 30 minutes the program
             * will grab new data from the API as there is a 333 calls per day limit. Each API call will use 6 credits to 
             * grab all the data required, which means 288 credits a day at once every half an hour. New data will be 
             * compared to old data and displayed visually through colours of the data in the table. The greener a value,
             * the higher it is compared to old data, and red for vice versa (or in the case of percentages, positive percentages 
             * are green and negative are red). The program grabs the top 20 coins currently, as well as any extra coins as 
             * specified in a text document on my computer so that it can be easily changed in the future.
             */
            while (true)
            {
                //Execute the update method of the instantiated class every 5 seconds
                mCrypto.Update();
                Thread.Sleep(5000);
            }
        }

        /* A static method to deep copy a dictionary 
         */
        public static Dictionary<TKey, TValue> CloneDictionaryCloningValues<TKey, TValue>(Dictionary<TKey, TValue> original) where TValue : ICloneable
        {
            Dictionary<TKey, TValue> ret = new Dictionary<TKey, TValue>(original.Count,
                                                                    original.Comparer);
            foreach (KeyValuePair<TKey, TValue> entry in original)
            {
                ret.Add(entry.Key, (TValue)entry.Value.Clone());
            }
            return ret;
        }
    }
}