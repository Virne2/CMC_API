using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using static System.Console;
using System.Diagnostics;
using System.Web;
using Newtonsoft.Json.Linq;

namespace CMC_API_Requester
{
    public class Crypto
    {
        //Number of times to try and grab data from the API should an error be returned. After this many tries, give up on requesting data
        //until the next time
        const int MAX_TRIES = 5;

        //Defining some colours to shorten code in IDE
        const ConsoleColor GREEN = ConsoleColor.Green;
        const ConsoleColor DARKGREEN = ConsoleColor.DarkGreen;
        const ConsoleColor RED = ConsoleColor.Red;
        const ConsoleColor DARKRED = ConsoleColor.DarkRed;
        const ConsoleColor YELLOW = ConsoleColor.Yellow;
        const ConsoleColor WHITE = ConsoleColor.White;
        const ConsoleColor CYAN = ConsoleColor.Cyan;

        //Defining the thresholds for the colours when displaying data vs previous data.
        static ConsoleColor[] ConsoleColorThresholdColours = { RED, DARKRED, WHITE, DARKGREEN, GREEN };
        static decimal[] ConsoleColorThresholdsDecimals = { 0.95M, 0.98M, 1.02M, 1.05M }; //When dividing a current value by a previous value. 1 = Same value, < 1 = value went down, > 1 = value went up
        static decimal[] ConsoleColorThresholdsPercentages = { -15, -5, 5, 15 };//Percentage thresholds for colours

        //List of API keys and an index to cycle through each API key in order so to balance out calls across each key
        private List<string> API_Keys;
        private int API_KeyIndex;

        //Number of top coins to be listed automatically
        const int ListingsRequestLimit = 20;

        //Delay between each API call, set to 30 minutes as this gives 288 calls a day which is under the 333 soft cap
        const int CallingDelayMinutes = 30;

        //DateTime values for when program is started, when the last API call was and when the next one should occur
        public DateTime StartTime;
        public DateTime LastAPICallTime;
        public DateTime NextAPICallTime;

        //Dictionaries of string to JObject which will be in the format <Coin name, data for coin>. When new data is obtained, CurrentData will be
        //deep cloned to PreviousData and the new data will be placed into CurrentData
        public Dictionary<string, JObject> CurrentData;
        public Dictionary<string, JObject> PreviousData;

        //List of coins that are requested from the txt file on my computer
        public List<string> CoinsToBeRequested;

        //Defining the format for column headers, width in characters and left/right alignment for the tabulated data
        public static string[] ColumnTitles = { "Rank", "Name", "Symbol", "Price BTC", "Price AUD", "Price USD", "Change 1HR", "Change 24HR", "Change 7D", "Market Cap USD" };
        public static int[] ColumnWidths = { 5, 18, 9, 14, 14, 14, 14, 14, 14, 23 };
        public static bool[] ColumnAligning = { true, true, true, true, true, true, false, false, false, false };
        private ColourString[] ColumnTitlesStrings;

        //When new data is displayed, cache the displayed strings so they do not need to be recalculated
        private List<ColourString[]> PreviousDisplayStrings;

        //Tally the number of credits used over time
        public int CreditsUsed;

        //Main constructor
        public Crypto()
        {
            //Set the start time to now
            StartTime = DateTime.Now;

            //Form the column headers and save them into ColumnTitlesStrings
            //Lines of strings that are to be printed into the table format are created as a list/array of ColourStrings.
            //ColourStrings are a struct having attributes for text, text width, colour and left/right alignment.
            //When printing a line, go through each ColourString in the array and print it in the designated colour, alignment and column width
            ColumnTitlesStrings = new ColourString[ColumnTitles.Length];
            for (int i = 0; i < ColumnTitles.Length; i++)
            {
                ColumnTitlesStrings[i] = new ColourString(ColumnTitles[i], ColumnWidths[i], ColumnAligning[i]);
            }

            //Initialize Dictionaries and lists
            CurrentData = new Dictionary<string, JObject>();
            PreviousData = new Dictionary<string, JObject>();
            PreviousDisplayStrings = new List<ColourString[]>();

            //Try gets coin symbols to be specifically requested from the txt file, throw exception if this fails for some reason
            if (GetCoinsToBeRequestedFromFile())
            {
                //We got the coin names
            }
            else
            {
                throw new Exception("Failed getting coin names");
            }

            //Try gets API keys to be specifically requested from the txt file, throw exception if this fails for some reason
            if (GetAPIKeysFromFile())
            {
                //We got the keys, we good
            }
            else
            {
                //Error getting keys, throw error
                throw new Exception("Failed getting API Keys");
            }

            //Set credits used to 0
            CreditsUsed = 0;
        }

        /// <summary>
        /// Grabs txt file and reads coins to be requested into the CoinsToBeRequested list.
        /// </summary>
        /// <returns>true is succeeded, false if failed for any reason</returns>
        public bool GetCoinsToBeRequestedFromFile()
        {
            //Initialize the list
            CoinsToBeRequested = new List<string>();
            try
            {
                //This is the path to the file
                var path = @"F:\\CMC_API_Requester\\coin-names.txt";
                using (StreamReader reader = new StreamReader(path))
                {
                    //Start reading
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        //Read each string into list
                        CoinsToBeRequested.Add(line);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                //Something failed
                WriteLine("Error Fetching Coin Names from txt: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Grabs txt file and reads APIKeys into the API_Keys list.
        /// </summary>
        /// <returns>true is succeeded, false if failed for any reason</returns>
        public bool GetAPIKeysFromFile()
        {
            //Initialize list and index
            API_Keys = new List<string>();
            API_KeyIndex = 0;
            try
            {
                //This is the path to the file
                var path = @"F:\\CMC_API_Requester\\API_KEY.txt";
                using (StreamReader reader = new StreamReader(path))
                {
                    //Start reading
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        //Read each string into the list
                        API_Keys.Add(line);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                //Something failed
                WriteLine("Error Fetching API Keys from txt: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Main Update for program, will be called every 5 seconds. If the current time is past the time for a new API call,
        /// make the call and update the data. If not, re-display the current data as Repeat. If there is an error anywhere,
        /// display the error display.
        /// </summary>
        public void Update()
        {
            //Check if a new API call needs to be made, either a new one or the first one
            if (DateTime.Now.CompareTo(NextAPICallTime) >= 0 || LastAPICallTime == DateTime.MinValue)
            {
                //If we succeed grabbing data, display it
                if (GetAPIData())
                {
                    Display(DisplayEnum.Default);
                }
                else
                {
                    //Failed getting API data, display error
                    Display(DisplayEnum.Error);
                }
            }
            else
            {
                //No new data, redisplay the last tabulated data
                Display(DisplayEnum.Repeat);
            }
        }

        /// <summary>
        /// Calls for new API data and loads it into CurrentData dictionary. 
        /// </summary>
        /// <returns>true if succeeded, false if failed</returns>
        public bool GetAPIData()
        {
            //Initialize a dictionary for temporarily storing the new data
            Dictionary<string, JObject> CallDataDict = new Dictionary<string, JObject>();

            //Set keys for dictionary, one key per converted currency for both top listings and specific quotes
            CallDataDict["ListingJsonDataBTC"] = new JObject();
            CallDataDict["ListingJsonDataUSD"] = new JObject();
            CallDataDict["ListingJsonDataAUD"] = new JObject();
            CallDataDict["QuotesJsonDataBTC"] = new JObject();
            CallDataDict["QuotesJsonDataUSD"] = new JObject();
            CallDataDict["QuotesJsonDataAUD"] = new JObject();

            //Start a loop trying to grab data. If we fail too many times just give up on getting data and try again in 30 minutes
            bool GettingData = true;
            int tries = 0;
            while (GettingData)
            {
                tries++;
                if (tries > MAX_TRIES)
                {
                    //Too many failures, set next API call time and give up this time
                    NextAPICallTime = DateTime.Now.AddMinutes(CallingDelayMinutes);
                    return false;
                }
                try
                {
                    //Call data in each converted currency for both listings and quotes
                    CallDataDict["ListingJsonDataBTC"] = GetAPICallListings("BTC");
                    CallDataDict["ListingJsonDataUSD"] = GetAPICallListings("USD");
                    CallDataDict["ListingJsonDataAUD"] = GetAPICallListings("AUD");
                    CallDataDict["QuotesJsonDataBTC"] = GetAPICallQuotes(CoinsToBeRequested, "BTC");
                    CallDataDict["QuotesJsonDataUSD"] = GetAPICallQuotes(CoinsToBeRequested, "USD");
                    CallDataDict["QuotesJsonDataAUD"] = GetAPICallQuotes(CoinsToBeRequested, "AUD");
                    //If we got to here, we successfully grabbed the data from the website. This does not mean the data is correct or error free, just that we successfully grabbed it.
                    //Set the next API call to half an hour from now.
                    GettingData = false;
                    LastAPICallTime = DateTime.Now; //Latest call is now
                    NextAPICallTime = DateTime.Now.AddMinutes(CallingDelayMinutes);
                }
                catch (WebException ex)
                {
                    //Something went wrong, check what kind of error was returned by coinmarketcap.com
                    using (StreamReader r = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        string responseContent = r.ReadToEnd();
                        JObject responseData = JsonConvert.DeserializeObject<JObject>(responseContent);

                        switch ((int)responseData["status"]["error_code"])
                        {
                            case (429):
                                //429, too many requests, try again in 30 minutes
                                NextAPICallTime = DateTime.Now.AddMinutes(CallingDelayMinutes);
                                return false;
                            case (500):
                                //500 internal server error, wait 1 minute and try again
                                NextAPICallTime = DateTime.Now.AddMinutes(1);
                                return false;
                            default:
                                //Other error, try again in 30 minutes
                                NextAPICallTime = DateTime.Now.AddMinutes(CallingDelayMinutes);
                                return false;
                        }
                    }
                }
            }
            //We got to here without failing to retrive data, check if any of the returned data is incorrect data
            foreach (KeyValuePair<string, JObject> entry in CallDataDict)
            {
                if ((int)entry.Value["status"]["error_code"] != 0)
                {
                    //There was an error in the data that was retrieved, bail out
                    return false;
                }
            }
            //No errors in the retrieved data, clone the old data into PreviousData
            PreviousData = Program.CloneDictionaryCloningValues(CurrentData);
            //For each coin in the listings data, compile it into a JObject and save it into the CurrentData dictionary as <coin name, JSON data>
            for (int i = 0; i < ListingsRequestLimit; i++)
            {
                JObject JData = (JObject)CallDataDict["ListingJsonDataBTC"]["data"][i]; //Grab data for this coin from BTC conversion
                ((JObject)(JData["quote"])).Add("USD", CallDataDict["ListingJsonDataUSD"]["data"][i]["quote"]["USD"]); //Add the USD conversion
                ((JObject)(JData["quote"])).Add("AUD", CallDataDict["ListingJsonDataAUD"]["data"][i]["quote"]["AUD"]); //Add the AUD conversion
                CurrentData[(string)CallDataDict["ListingJsonDataBTC"]["data"][i]["slug"]] = JData; //Set compiled data into the dictionary
            }
            //Do the same for each quoted coin
            for (int i = 0; i < CoinsToBeRequested.Count; i++)
            {
                //If the dictionary already contains this quoted coin, it means it's in the top coins and does not need to be added again
                if (!CurrentData.ContainsKey((string)CallDataDict["QuotesJsonDataBTC"]["data"][CoinsToBeRequested[i]]["symbol"]))
                {
                    JObject JData = (JObject)CallDataDict["QuotesJsonDataBTC"]["data"][CoinsToBeRequested[i]]; //Grab data for this coin from BTC conversion
                    ((JObject)(JData["quote"])).Add("USD", CallDataDict["QuotesJsonDataUSD"]["data"][CoinsToBeRequested[i]]["quote"]["USD"]); //Add the USD conversion
                    ((JObject)(JData["quote"])).Add("AUD", CallDataDict["QuotesJsonDataAUD"]["data"][CoinsToBeRequested[i]]["quote"]["AUD"]); //Add the AUD conversion
                    CurrentData[(string)CallDataDict["QuotesJsonDataBTC"]["data"][CoinsToBeRequested[i]]["slug"]] = JData; //Set compiled data into the dictionary
                }
            }
            //We successfully grabbed the data
            return true;
        }

        /// <summary>
        /// Method to make an API call for the top listings of coins. Coinmarketcap will return the data as json formatted data as a string,
        /// so it needs to be converted to the Newtonsoft JObject format to manipulate it as JSON data.
        /// </summary>
        /// <param name="convert">Currency symbol for the data to be converted to automatically</param>
        /// <returns>API data in JSON formatted JObject</returns>
        public JObject GetAPICallListings(string convert)
        {
            //How to grab listings data from https://pro.coinmarketcap.com/account according to their documentation
            var CMC_URL = new UriBuilder("https://pro-api.coinmarketcap.com/v1/cryptocurrency/listings/latest");

            var queryString = HttpUtility.ParseQueryString(string.Empty);
            queryString["start"] = "1";
            queryString["limit"] = ListingsRequestLimit.ToString();
            queryString["convert"] = convert;

            CMC_URL.Query = queryString.ToString();

            var client = new WebClient();
            client.Headers.Add("X-CMC_PRO_API_KEY", GetAPIKey());
            client.Headers.Add("Accepts", "application/json");
            try
            {
                //Try convert the grabbed data into a JSON format
                JObject output = JsonConvert.DeserializeObject<JObject>(client.DownloadString(CMC_URL.ToString()));
                //Record the number of credits used
                CreditsUsed += (int)output["status"]["credit_count"];
                return output;
            }
            catch (WebException ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Method to make an API call for specific quotes of coins. Coinmarketcap will return the data as json formatted data as a string,
        /// so it needs to be converted to the Newtonsoft JObject format to manipulate it as JSON data.
        /// </summary>
        /// <param name="coins">List of specific coins to be requested</param>
        /// <param name="convert">Currency symbol for the data to be converted to automatically</param>
        /// <returns>API data in JSON formatted JObject</returns>
        public JObject GetAPICallQuotes(List<string> coins, string convert)
        {
            //How to grab quotes data from https://pro.coinmarketcap.com/account according to their documentation
            var CMC_URL = new UriBuilder("https://pro-api.coinmarketcap.com/v1/cryptocurrency/quotes/latest");

            var queryString = HttpUtility.ParseQueryString(string.Empty);
            queryString["symbol"] = string.Join(",", coins);
            queryString["convert"] = convert;

            CMC_URL.Query = queryString.ToString();

            var client = new WebClient();
            client.Headers.Add("X-CMC_PRO_API_KEY", GetAPIKey());
            client.Headers.Add("Accepts", "application/json");
            try
            {
                //Try convert the grabbed data into a JSON format
                JObject output = JsonConvert.DeserializeObject<JObject>(client.DownloadString(CMC_URL.ToString()));
                //Record the number of credits used
                CreditsUsed += (int)output["status"]["credit_count"];
                return output;
            }
            catch (WebException ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Method to grab an API key that is being cycled so each API key gets equal calls
        /// </summary>
        /// <returns>An API key as a string</returns>
        public string GetAPIKey()
        {
            API_KeyIndex = (API_KeyIndex + 1) % API_Keys.Count; //Cycle index
            return API_Keys[API_KeyIndex];
        }

        /// <summary>
        /// Main display function to print to screen
        /// </summary>
        /// <param name="arg">Display arg on what to show, either new data, old data or an error</param>
        public void Display(DisplayEnum arg)
        {
            //Clear the screen and set the default colour
            Clear();
            ForegroundColor = WHITE;
            //Write some basic information to the top of the console
            WriteLine("Monitor started at {0}", StartTime.ToLocalTime().ToString()); //Time program was started
            WriteLine("Current time is {0}, next update at {1} (in {2})",
                DateTime.Now.ToLocalTime().ToString(),
                NextAPICallTime.ToLocalTime().ToString(),
                NextAPICallTime.Subtract(DateTime.Now).ToString("mm\\:ss")); //Current time and time till next update
            WriteLine("Credits Used: {0} ({1}/HR, {2}/Day)",
                CreditsUsed,
                Math.Round(CreditsUsed / DateTime.Now.Subtract(StartTime).TotalHours, 2),
                Math.Round(CreditsUsed / DateTime.Now.Subtract(StartTime).TotalDays, 2)); //How many credits we've used so far with approximate rates
            WriteLine(); //Spacing

            //If the arg was to display error, write it
            if (arg == DisplayEnum.Error)
            {
                WriteLine("Error while grabbing data.");
                return;
            }
            //If the arg was to repeat the last data that was displayed, re-display the cached ColourStrings so no new calculations need to be made
            else if (arg == DisplayEnum.Repeat)
            {
                //Display headers
                DisplayColourTextLine(ColumnTitlesStrings);
                //Display each line as they were before
                foreach (ColourString[] CSArray in PreviousDisplayStrings)
                {
                    ForegroundColor = WHITE;
                    DisplayColourTextLine(CSArray);
                }
            }
            //We have new data, calcaulate values and colours to be displayed. First check that data is not empty for some reason
            else if (CurrentData.Count != 0)
            {
                //Data is not empty, lets start displaying. First, write a line for the headers at the top of the table
                DisplayColourTextLine(ColumnTitlesStrings);
                //Clear the previous cached strings for the table, we will write the new ones so that the values do not need to be calculated every 5 seconds
                PreviousDisplayStrings.Clear();
                //For each coin in our CurrentData dictionary, display a line in the table for the data on this coin
                foreach (KeyValuePair<string, JObject> entry in CurrentData)
                {
                    //Check if we have previous data on this coin to make comparisons to
                    bool PreviousDataExists = PreviousData.ContainsKey(entry.Key);
                    //Set a temporary colour to be used throughout this line
                    ConsoleColor tempColor = WHITE;
                    //Create a ColourString array to place the generated ColourStrings into
                    ColourString[] CoinColourStrings = new ColourString[10];
                    //If we specifically requested this coin to be quoted, make it's rank, name and symbol cyan coloured
                    if (CoinsToBeRequested.Contains((string)entry.Value["symbol"]))
                    {
                        tempColor = CYAN;
                    }
                    //ColourStrings for rank, name and symbol
                    CoinColourStrings[0] = new ColourString((string)entry.Value["cmc_rank"], ColumnWidths[0], tempColor, true);
                    CoinColourStrings[1] = new ColourString((string)entry.Value["name"], ColumnWidths[1], tempColor, true);
                    CoinColourStrings[2] = new ColourString((string)entry.Value["symbol"], ColumnWidths[2], tempColor, true);

                    //Check if price in currencies is higher than previous data and set colour accordingly
                    tempColor = PreviousDataExists ? GetConsoleColorRatio((decimal)entry.Value["quote"]["BTC"]["price"] / (decimal)PreviousData[entry.Key]["quote"]["BTC"]["price"]) : WHITE;
                    CoinColourStrings[3] = new ColourString((Math.Round((decimal)entry.Value["quote"]["BTC"]["price"], 9)).ToString(), ColumnWidths[3], tempColor, true);

                    tempColor = PreviousDataExists ? GetConsoleColorRatio((decimal)entry.Value["quote"]["AUD"]["price"] / (decimal)PreviousData[entry.Key]["quote"]["AUD"]["price"]) : WHITE;
                    CoinColourStrings[4] = new ColourString("$" + (Math.Round((decimal)entry.Value["quote"]["AUD"]["price"], 2)).ToString(), ColumnWidths[4], tempColor, true);

                    tempColor = PreviousDataExists ? GetConsoleColorRatio((decimal)entry.Value["quote"]["USD"]["price"] / (decimal)PreviousData[entry.Key]["quote"]["USD"]["price"]) : WHITE;
                    CoinColourStrings[5] = new ColourString("$" + (Math.Round((decimal)entry.Value["quote"]["USD"]["price"], 2)).ToString(), ColumnWidths[5], tempColor, true);

                    //Check if percent changes over time are positive or negative and set colour accordingly
                    tempColor = GetConsoleColorPercentages((decimal)entry.Value["quote"]["USD"]["percent_change_1h"]);
                    CoinColourStrings[6] = new ColourString(Math.Round((decimal)entry.Value["quote"]["USD"]["percent_change_1h"], 2) + "%", ColumnWidths[6], tempColor, false);

                    tempColor = GetConsoleColorPercentages((decimal)entry.Value["quote"]["USD"]["percent_change_24h"]);
                    CoinColourStrings[7] = new ColourString(Math.Round((decimal)entry.Value["quote"]["USD"]["percent_change_24h"], 2) + "%", ColumnWidths[7], tempColor, false);

                    tempColor = GetConsoleColorPercentages((decimal)entry.Value["quote"]["USD"]["percent_change_7d"]);
                    CoinColourStrings[8] = new ColourString(Math.Round((decimal)entry.Value["quote"]["USD"]["percent_change_7d"], 2) + "%", ColumnWidths[8], tempColor, false);

                    //Check if market cap in USD is higher than previous data and set colour accordingly
                    tempColor = PreviousDataExists ? GetConsoleColorRatio((decimal)entry.Value["quote"]["USD"]["market_cap"] - (decimal)PreviousData[entry.Key]["quote"]["USD"]["market_cap"]) : WHITE;
                    CoinColourStrings[9] = new ColourString("$" + string.Format("{0:n}", Math.Round((decimal)entry.Value["quote"]["USD"]["market_cap"], 0)), ColumnWidths[9], tempColor, false);

                    //Cache this line
                    PreviousDisplayStrings.Add(CoinColourStrings);
                    //Display this line
                    DisplayColourTextLine(CoinColourStrings);
                }
            }
            else
            {
                //No data
                WriteLine("No data to be displayed :(");
            }
        }

        /// <summary>
        /// Method to display a line of ColourStrings onto the screen, with each individual string being coloured and padded correctly. This method does not put a "/n" at the end.
        /// </summary>
        /// <param name="strings">ColourStrings to be written to screen</param>
        public void DisplayColourText(params ColourString[] strings)
        {
            //Remember current ForegroundColour
            ConsoleColor originalColor = ForegroundColor;
            //Write each string to screen
            foreach (var str in strings)
            {
                //Set colour this string wants to be
                ForegroundColor = str.Colour;
                //Truncate this string to fit into the column width
                string PrintString = TruncateString(str.AlignLeft ? string.Format("{0}", str.Text).PadRight(str.TextWidth) : string.Format("{0}", str.Text).PadLeft(str.TextWidth), str.TextWidth);
                //Write it
                Write(PrintString);
            }
            //Reset old colour
            ForegroundColor = originalColor;
        }

        /// <summary>
        /// Method to display a line of ColourStrings onto the screen, with each individual string being coloured and padded correctly. This method writes a new line at the end.
        /// </summary>
        /// <param name="strings">ColourStrings to be written to screen</param>
        public void DisplayColourTextLine(params ColourString[] strings)
        {
            //Remember current ForegroundColour
            ConsoleColor originalColor = ForegroundColor;
            //Write each string to screen
            foreach (var str in strings)
            {
                //Set colour this string wants to be
                ForegroundColor = str.Colour;
                //Truncate this string to fit into the column width
                string PrintString = TruncateString(str.AlignLeft ? string.Format("{0}", str.Text).PadRight(str.TextWidth) : string.Format("{0}", str.Text).PadLeft(str.TextWidth), str.TextWidth);
                //Write it
                Write(PrintString);
            }
            //Reset old colour
            ForegroundColor = originalColor;
            WriteLine();
        }

        /// <summary>
        /// Get the colour a string should be according to the ratio the value is compared to old data. Red means decreased value, green means increased
        /// </summary>
        /// <param name="input">Input ratio to be tested</param>
        /// <returns>Colour that string should be</returns>
        public ConsoleColor GetConsoleColorRatio(decimal input)
        {
            //For each threshold, if below it return the colour
            for (int i = 0; i < ConsoleColorThresholdsDecimals.Length; i++)
            {
                if (input <= ConsoleColorThresholdsDecimals[i])
                {
                    return ConsoleColorThresholdColours[i];
                }
            }
            //Not below any thresholds so must be in highest category, set highest colour
            return ConsoleColorThresholdColours[ConsoleColorThresholdColours.Length - 1];
        }

        /// <summary>
        /// Get the colour a string should be according to the percentage. Red means negative percentage, green means positive
        /// </summary>
        /// <param name="input">Input percentage to be tested</param>
        /// <returns>Colour that string should be</returns>
        public ConsoleColor GetConsoleColorPercentages(decimal input)
        {
            //For each threshold, if below it return the colour
            for (int i = 0; i < ConsoleColorThresholdsPercentages.Length; i++)
            {
                if (input <= ConsoleColorThresholdsPercentages[i])
                {
                    return ConsoleColorThresholdColours[i];
                }
            }
            //Not below any thresholds so must be in highest category, set highest colour
            return ConsoleColorThresholdColours[ConsoleColorThresholdColours.Length - 1];
        }

        /// <summary>
        /// Method to truncate a string to certain length to fit in a column
        /// </summary>
        /// <param name="input">Input string</param>
        /// <param name="maxLength">Maximum length string can be</param>
        /// <returns>Truncated string</returns>
        public static string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            return input.Length <= maxLength ? input : input.Substring(0, maxLength);
        }
    }
}

/// <summary>
/// Basic struct to contain data on what colour a string should be when printed, how it should be padded and the column width
/// </summary>
public struct ColourString
{
    public string Text; //String to be printed to screen
    public int TextWidth; //Maximum width of this column
    public ConsoleColor Colour; //Colour this string should be
    public bool AlignLeft; //Padding

    /// <summary>
    /// Constructor with no colour, assumes colour will be white
    /// </summary>
    /// <param name="text">String to be written</param>
    /// <param name="TWidth">Maximum width of string</param>
    /// <param name="padLeft">Padding, true = left, false = right</param>
    public ColourString(string text, int TWidth, bool padLeft)
    {
        Text = text;
        TextWidth = TWidth;
        Colour = ConsoleColor.White;
        AlignLeft = padLeft;
    }

    /// <summary>
    /// Constructor with colour
    /// </summary>
    /// <param name="text">String to be written</param>
    /// <param name="TWidth">Maximum width of string</param>
    /// <param name="colour">Colour this string should be</param>
    /// <param name="padLeft">Padding, true = left, false = right</param>
    public ColourString(string text, int TWidth, ConsoleColor colour, bool padLeft)
    {
        Text = text;
        TextWidth = TWidth;
        Colour = colour;
        AlignLeft = padLeft;
    }
}

/// <summary>
/// Basic display type enum
/// </summary>
public enum DisplayEnum { Default, Error, Repeat }