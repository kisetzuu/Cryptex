using System;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using static User;
using Newtonsoft.Json.Linq;
using System.Timers;
using static Cryptocurrency;

// Defining the transaction interface
public interface ITransaction
{
    void Deposit(double amount);
    bool Withdraw(double amount);
}

public abstract class Cryptocurrency : ITransaction
{
    private double balance;
    public double Balance
    {
        get { return balance; }
        protected set { balance = value; }
    }

    private static Dictionary<string, (decimal Price, DateTime Timestamp)> priceCache = new Dictionary<string, (decimal, DateTime)>();
    private static TimeSpan cacheDuration = TimeSpan.FromMinutes(5);
    public double CurrentPrice { get; private set; }

    public async Task InitializeAsync()
    {
        await UpdatePriceAsync();
    }

    public async Task UpdatePriceAsync()
    {
        string coinId = this.GetType().Name.ToLower();

        // Check cache first
        if (priceCache.TryGetValue(coinId, out var cachedValue))
        {
            if (DateTime.UtcNow - cachedValue.Timestamp < cacheDuration)
            {
                CurrentPrice = (double)cachedValue.Price;
                return;
            }
        }

        try
        {
            CurrentPrice = (double)await FetchPriceWithRetry(coinId);
            priceCache[coinId] = ((decimal)CurrentPrice, DateTime.UtcNow); // Explicit cast to decimal
            DisplayUtils.DisplayMessage("Price data fetched successfully.");
        }
        catch (Exception ex)
        {
            DisplayUtils.DisplayError($"Failed to fetch price data: {ex.Message}");
        }
    }
    public async Task<decimal> FetchPriceWithRetry(string coinId)
    {
        int maxRetryAttempts = 3;
        int retryDelayMilliseconds = 1000; // Initial delay, will be increased exponentially

        for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
        {
            try
            {
                return await FetchPriceFromCoinGecko(coinId);
            }
            catch (HttpRequestException)
            {
                if (attempt == maxRetryAttempts) throw;
                await Task.Delay(retryDelayMilliseconds);
                retryDelayMilliseconds *= 2; // Exponential increase
            }
        }

        throw new Exception("Unable to fetch price after retries.");
    }

    public Cryptocurrency()
    {
    }

    public virtual void Deposit(double amount)
    {
        Balance += amount;
    }

    public virtual bool Withdraw(double amount)
    {
        if (Balance >= amount)
        {
            Balance -= amount;
            return true;
        }
        return false;
    }

    public interface ICryptocurrency
    {
        Task UpdatePriceAsync();
    }

    public static class DisplayUtils
    {
        public static void DisplayMessage(string message)
        {
            Console.Clear();
            Console.WriteLine(message);
        }

        public static void DisplayError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }


    public interface IPriceFetcher
    {
        Task<decimal> FetchPriceWithRetry(string coinId);
        Task<decimal> FetchPriceFromCoinGecko(string coinId);
    }


    protected async Task<decimal> FetchPriceFromCoinGecko(string coinId)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            string apiUrl = $"https://api.coingecko.com/api/v3/simple/price?ids={coinId}&vs_currencies=usd";

            // Display initial loading message
            Console.WriteLine("Fetching price data from CoinGecko...");

            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                // Update loading message upon successful fetch
                Console.WriteLine("Price data fetched successfully.");

                dynamic data = JsonConvert.DeserializeObject(responseBody);
                return (decimal)data[coinId]["usd"];
            }
            catch (HttpRequestException ex)
            {
                // Update loading message if there's an error
                Console.WriteLine($"Failed to fetch price data: {ex.Message}");
                throw;
            }
        }
    }

    public class ACryptocurrency : Cryptocurrency, ITransaction, IPriceFetcher
    {
        public double CurrentPrice { get; private set; }

        // Implementing ITransaction methods
        public override void Deposit(double amount)
        {
            // Implementation of Deposit specific to ACryptocurrency
            base.Deposit(amount);
        }

        public override bool Withdraw(double amount)
        {
            // Implementation of Withdraw specific to ACryptocurrency
            return base.Withdraw(amount);
        }

        // Implementing IPriceFetcher methods
        public async Task<decimal> FetchPriceFromCoinGecko(string coinId)
        {
            // Specific implementation for ACryptocurrency, if needed
            return await base.FetchPriceFromCoinGecko(coinId);
        }

        public async Task<decimal> FetchPriceWithRetry(string coinId)
        {
            // Specific implementation for ACryptocurrency, if needed
            return await base.FetchPriceWithRetry(coinId);
        }
    }
}




// Specific Cryptocurrency classes
public class Bitcoin : Cryptocurrency { }
public class Ethereum : Cryptocurrency { }
public class Tether : Cryptocurrency { }

// User class definition
public class User
{
    public string Username { get; set; }
    private string HashedPassword { get; set; }
    public string Password { get; set; }
    public bool IsAdmin { get; set; }

    public void SetHashedPassword(string hashedPassword)
    {
        this.HashedPassword = hashedPassword;
    }

    public Cryptocurrency BitcoinWallet { get; } = new Bitcoin();
    public Cryptocurrency EthereumWallet { get; } = new Ethereum();
    public Cryptocurrency TetherWallet { get; } = new Tether();


    public void SetPassword(string password) => HashedPassword = PasswordSecurity.HashPassword(password);

    public bool Authenticate(string password) => HashedPassword == PasswordSecurity.HashPassword(password);

    public Dictionary<string, double> CryptoBalances { get; set; }

    public void UpdateBalanceBasedOnCurrentPrice(Dictionary<string, double> currentPrices)
    {
        foreach (var crypto in CryptoBalances.Keys.ToList())
        {
            if (currentPrices.ContainsKey(crypto))
            {
                CryptoBalances[crypto] *= currentPrices[crypto];
            }
        }
    }

    public async Task InitializeCryptocurrenciesAsync()
    {
        await BitcoinWallet.InitializeAsync();
        await EthereumWallet.InitializeAsync();
        await TetherWallet.InitializeAsync();
    }

    public string ToCsv()
    {
        return $"{Username},{HashedPassword},{BitcoinWallet.Balance},{EthereumWallet.Balance},{TetherWallet.Balance},{IsAdmin}";
    }


    public static User FromCsv(string csvLine)
    {
        string[] values = csvLine.Split(',');
        User user = new User
        {
            Username = values[0],
            HashedPassword = values[1],
            IsAdmin = bool.Parse(values[5])
        };

        user.BitcoinWallet.Deposit(double.Parse(values[2]));
        user.EthereumWallet.Deposit(double.Parse(values[3]));
        user.TetherWallet.Deposit(double.Parse(values[4]));

        return user;
    }


    public bool ValidatePassword(string inputPassword)
    {
        string hashedInput = ComputeSha256Hash(inputPassword);
        return hashedInput == this.Password;  // Compare hashed values
    }

    private static string ComputeSha256Hash(string rawData)
    {
        using (SHA256 sha256Hash = SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }

    public async Task DisplayBalances()
    {
        // Fetch data from CoinGecko here
        await BitcoinWallet.UpdatePriceAsync();
        await EthereumWallet.UpdatePriceAsync();
        await TetherWallet.UpdatePriceAsync();

        // Tether is typically pegged to USD, so it may not need an update, but you could update it in the same way if needed.

        // Calculate the total value in USD
        double bitcoinValueInUSD = BitcoinWallet.Balance * BitcoinWallet.CurrentPrice;
        double ethereumValueInUSD = EthereumWallet.Balance * EthereumWallet.CurrentPrice;
        double tetherValueInUSD = TetherWallet.Balance; // Assuming 1 Tether = 1 USD

        // Get the maximum label width
        int maxLabelWidth = Math.Max("Bitcoin:".Length, Math.Max("Ethereum:".Length, "Tether:".Length));

        // Create justified strings with USD values
        string bitcoinLine = $"{"Bitcoin:".PadRight(maxLabelWidth)} {BitcoinWallet.Balance} BTC = {bitcoinValueInUSD} USD";
        string ethereumLine = $"{"Ethereum:".PadRight(maxLabelWidth)} {EthereumWallet.Balance} ETH = {ethereumValueInUSD} USD";
        string tetherLine = $"{"Tether:".PadRight(maxLabelWidth)} {TetherWallet.Balance} USDT = {tetherValueInUSD} USD";

        // Display centered
        DisplayCentered(bitcoinLine);
        DisplayCentered(ethereumLine);
        DisplayCentered(tetherLine);
    }

    // You would also need to update the 'DisplayCentered' method if it does not already handle strings properly
    private void DisplayCentered(string line)
    {
        Console.WriteLine(line.PadLeft(Console.WindowWidth / 2 + line.Length / 2));
    }

    // Assuming 'BitcoinWallet', 'EthereumWallet', and 'TetherWallet' are instances of classes derived from 'Cryptocurrency'



    public void Deposit(Cryptocurrency crypto, double amount)
    {
        crypto.Deposit(amount);
    }

    public void Withdraw(Cryptocurrency crypto, double amount)
    {
        bool wasWithdrawalSuccessful = crypto.Withdraw(amount);

        if (!wasWithdrawalSuccessful)
        {
            Console.WriteLine("Insufficient funds.");
        }
    }

    public double ConvertCrypto(CryptoType from, CryptoType to, double amount)
    {
        double conversionRate = GetConversionRate(from, to);

        // Get the balance of the 'from' wallet to check if the user has sufficient funds
        double fromBalance = GetBalance(from);
        if (fromBalance < amount)
        {
            Console.WriteLine("Insufficient funds for conversion.");
            return 0; // Return 0 as an indication of failure
        }

        // Convert the amount using the conversion rate
        return amount * conversionRate;
    }

    public double GetBalance(CryptoType type)
    {
        switch (type)
        {
            case CryptoType.BTC: return BitcoinWallet.Balance;
            case CryptoType.ETH: return EthereumWallet.Balance;
            case CryptoType.USDT: return TetherWallet.Balance;
            default: throw new ArgumentException("Invalid CryptoType.");
        }
    }

    private double GetConversionRate(CryptoType from, CryptoType to)
    {
        // You would replace these hardcoded values with real-time market data in a real application.
        if (from == CryptoType.BTC && to == CryptoType.ETH)
            return 20; // Assume 1 BTC is 20 ETH
        else if (from == CryptoType.BTC && to == CryptoType.USDT)
            return 40000; // Assume 1 BTC is 40000 USDT
        else if (from == CryptoType.ETH && to == CryptoType.BTC)
            return 0.05; // Assume 1 ETH is 0.05 BTC
        else if (from == CryptoType.ETH && to == CryptoType.USDT)
            return 2000; // Assume 1 ETH is 2000 USDT
        else if (from == CryptoType.USDT && to == CryptoType.BTC)
            return 1 / 40000; // The inverse of 1 BTC to USDT
        else if (from == CryptoType.USDT && to == CryptoType.ETH)
            return 1 / 2000; // The inverse of 1 ETH to USDT
        else
            throw new ArgumentException("Invalid conversion pair.");
    }

    public static class FeeConstants
    {
        public const double BankingFee = 0.2 / 100; // 0.2%
        public const double ExchangeFee = 0.5 / 100; // 0.5%
    }
    public void UpdateWalletBalance(CryptoType cryptoType, double amount)
    {
        switch (cryptoType)
        {
            case CryptoType.BTC:
                BitcoinWallet.Deposit(amount);
                break;
            case CryptoType.ETH:
                EthereumWallet.Deposit(amount);
                break;
            case CryptoType.USDT:
                TetherWallet.Deposit(amount);
                break;
            default: throw new ArgumentException("Invalid CryptoType.");
        }
    }

    // CryptoPriceUpdater class
    public class CryptoPriceUpdater
    {
        private static readonly HttpClient client = new HttpClient();

        public async Task<double> GetCryptoPrice(string cryptoId)
        {
            string url = $"https://api.coingecko.com/api/v3/simple/price?ids={cryptoId}&vs_currencies=usd";
            try
            {
                string response = await client.GetStringAsync(url);
                var data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, double>>>(response);
                return data[cryptoId]["usd"];
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error fetching price: " + ex.Message);
                return -1;
            }
        }
    }


    // PasswordSecurity class definition
    public class PasswordSecurity
    {
        public static string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
    public enum CryptoType
    {
        BTC,
        ETH,
        USDT
    }

}

class Program
{
    // Full path to your Admins.csv file
    private static readonly string adminCsvFile = @"C:\Users\chris\Desktop\MIDTERM CPE\Admin\admin_credentials.csv";
    private static readonly string csvFile = @"C:\Users\chris\Desktop\MIDTERM CPE\Users\CSVSusers.csv";

    static List<User> users = new List<User>(); // This holds all the users.
    static List<User> admins = new List<User>(); // List to store admin users
    static User currentUser; // This represents the currently logged-in user (if any).
    static bool isAdmin = false; // Default to false at the start of the program


    static async Task Main(string[] args)
    {
        string csvFilePath = @"C:\Users\chris\Desktop\MIDTERM CPE\Users\CSVSusers.csv";

        if (File.Exists(csvFilePath) && new FileInfo(csvFilePath).Length > 0)
        {
            LoadUsersFromCsv(); // Load existing users
        }
        else
        {
            CreateCsvTemplate(csvFilePath); // Create a new CSV template if the file doesn't exist
        }

        foreach (var user in users)
        {
            await user.InitializeCryptocurrenciesAsync();
        }

        bool keepRunning = true;
        while (keepRunning)
        {
            Console.Clear();
            DisplayHeader();
            DisplayMenu();
            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    Register();
                    PauseForUser();
                    break;
                case "2":
                    Login(); // Regular user login
                    break;
                case "3":
                    AdminLogin(); // Admin login process
                    break;
                case "4":
                    DisplayHelp();
                    PauseForUser();
                    break;
                case "5":
                    keepRunning = false; // Exit the program
                    break;
                default:
                    Console.WriteLine("Invalid choice. Please select a valid option.");
                    PauseForUser();
                    break;
            }
        }

        SaveUsers(); // Save any changes to the CSV file
    }

    static void OnProcessExit(object sender, EventArgs e)
    {
        SaveUsers();
    }

    static void LoadUsersFromCsv()
    {
        string filePath = @"C:\Users\chris\Desktop\MIDTERM CPE\Users\CSVSusers.csv";

        if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines.Skip(1)) // Skipping the header row
            {
                User user = User.FromCsv(line);

                // Check if user already exists in the list to prevent duplicates
                if (!users.Any(u => u.Username == user.Username))
                {
                    users.Add(user);
                }
            }
        }
        else
        {
            CreateCsvTemplate(filePath); // Create a new CSV file with headers
        }
    }


    static void SaveUsers()
    {
        try
        {
            List<string> lines = new List<string>() { "Username,Password,BitcoinBalance,EthereumBalance,TetherBalance,IsAdmin" }; // Including header
            foreach (User user in users)
            {
                lines.Add(user.ToCsv());
            }
            File.WriteAllLines(csvFile, lines);
            Console.WriteLine("Users saved to CSV successfully."); // Confirmation message
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while saving users: " + ex.Message);
            throw; // Re-throwing the exception to handle it accordingly.
        }
    }

    static void CreateCsvTemplate(string filePath)
    {
        string[] headers = { "Username", "Password" }; // Add other headers as needed
        string headerLine = string.Join(",", headers);
        File.WriteAllText(filePath, headerLine + Environment.NewLine);
    }

    static void LoadAdmins()
    {
        try
        {
            // Check if file exists before attempting to read
            if (File.Exists(adminCsvFile))
            {
                string[] lines = File.ReadAllLines(adminCsvFile);
                foreach (string line in lines)
                {
                    User admin = User.FromCsv(line);
                    admins.Add(admin);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while loading admins: " + ex.Message);
        }
    }


    static void SaveAdmins()
    {
        try
        {
            // Ensure the directory exists
            string directoryPath = @"C:\Users\chris\Desktop\MIDTERM CPE\Admin\admin_credentials.csv";
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Convert user data to CSV format and save it
            List<string> lines = admins.Select(admin => admin.ToCsv()).ToList();
            File.WriteAllLines(adminCsvFile, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while saving admins: " + ex.Message);
        }
    }



    static void PauseForUser()
    {
        Console.WriteLine();
        Console.WriteLine("Press ENTER to continue...");
        Console.ReadLine();
    }

    public class CryptoOperations
    {
        private static Dictionary<(User.CryptoType, User.CryptoType), double> conversionRates = new Dictionary<(User.CryptoType, User.CryptoType), double>
    {
        {(User.CryptoType.BTC, User.CryptoType.ETH), 20},
        {(User.CryptoType.BTC, User.CryptoType.USDT), 45000},
        {(User.CryptoType.ETH, User.CryptoType.BTC), 0.05},
        {(User.CryptoType.ETH, User.CryptoType.USDT), 2250},
        {(User.CryptoType.USDT, User.CryptoType.BTC), 0.000022},
        {(User.CryptoType.USDT, User.CryptoType.ETH), 0.000444},
    };

        public static double GetConversionRate(User.CryptoType from, User.CryptoType to)
        {
            if (conversionRates.TryGetValue((from, to), out double rate))
            {
                return rate;
            }
            else
            {
                // Handle the case where there is no direct conversion rate available
                throw new InvalidOperationException($"No conversion rate available from {from} to {to}");
            }
        }
    }

    static void ViewAllUsers()
    {
        Console.Clear();

        string filePath = @"C:\Users\chris\Desktop\MIDTERM CPE\Users\CSVSusers.csv"; // Path to your CSV

        // Check if the file exists
        if (!File.Exists(filePath))
        {
            Console.WriteLine("Error: File not found.");
            return;
        }

        // Read all lines from the CSV file
        string[] lines = File.ReadAllLines(filePath);

        // Assuming fixed column widths based on your formatting
        int tableWidth = 76; // Total width of your table
        int windowWidth = Console.WindowWidth;
        int leftPadding = (windowWidth - tableWidth) / 2;

        // Calculate the exact center for "List of Users"
        string title = "List of Users";
        int titleLength = title.Length;
        int titlePadding = (tableWidth - titleLength) / 2 - 2; // Subtract 2 for the border characters

        // Print the centered box with the title "List of Users"
        PrintCentered("╔" + new string('═', tableWidth - 2) + "╗", leftPadding);
        PrintCentered("║" + new string(' ', titlePadding) + title + new string(' ', titlePadding) + "   ║", leftPadding);
        PrintCentered("╚" + new string('═', tableWidth - 2) + "╝", leftPadding);

        // Centering the header and separator line - This should also be outside and before the loop
        string header = String.Format("{0,-15} | {1,15} | {2,15} | {3,15}", "Username", "Bitcoin Balance", "Ethereum Balance", "Tether Balance");
        PrintCentered(header, leftPadding);
        PrintCentered(new string('-', tableWidth), leftPadding);

        // Iterate over the lines, skip the first one if it's a header
        for (int i = 1; i < lines.Length; i++) // Change '1' to '0' if there's no header row in the CSV
        {
            string[] values = lines[i].Split(',');

            // Adjust the indices based on the actual structure of your CSV
            string username = values[0];
            string bitcoinBalance = values[2];
            string ethereumBalance = values[3];
            string tetherBalance = values[4];

            string line = String.Format("{0,-15} | {1,15} | {2,15} | {3,15}", username, bitcoinBalance, ethereumBalance, tetherBalance);
            PrintCentered(line, leftPadding);
        }

    }


    static void PrintCentered(string text, int leftPadding)
    {
        Console.WriteLine(new string(' ', leftPadding) + text);
    }

    public static User GetAdminCredentials()
    {
        string filePath = @"C:\Users\chris\Desktop\MIDTERM CPE\Admin\admin_credentials.csv";

        try
        {
            string[] lines = File.ReadAllLines(filePath);

            if (lines.Length < 2)
            {
                Console.WriteLine("No admin credentials found in the file.");
                return null;
            }

            string[] credentials = lines[1].Split(',');

            if (credentials.Length == 2)
            {
                User adminUser = new User
                {
                    Username = credentials[0].Trim(),
                    Password = credentials[1].Trim()
                };

                // For debugging: Log the username (but not the password)
                Console.WriteLine($"Admin username: {adminUser.Username}");

                return adminUser;
            }
            else
            {
                Console.WriteLine("Admin credentials in the file are improperly formatted.");
                return null;
            }
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine("Admin credentials file not found: " + ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while reading the admin credentials: " + ex.Message);
            return null;
        }
    }

    static void AdminLogin()
    {
        Console.Clear(); // Clears the console screen before displaying the login interface

        DisplayCentered("╔═════════════════════════╗");
        DisplayCentered("║       Admin Login       ║");
        DisplayCentered("╚═════════════════════════╝");

        DisplayCentered(""); // For an empty line
        DisplayCentered("Admin Username: ", false);
        string username = Console.ReadLine();

        DisplayCentered("Admin Password: ", false);
        string password = ReadPassword(); // Assuming you have a method for password input

        // This is a very basic check. In real-world scenarios, you should NEVER store or compare passwords in plain text.
        if (username == "admin" && password == "admin") // please change "admin" and "password" to something more secure
        {
            isAdmin = true;  // Set this immediately after verifying the admin credentials
            Console.Clear(); // Optionally clear console again to transition to the admin panel interface
            Console.WriteLine("Login successful.");
            // Here you can call whatever function you want the admin to perform
            AdminPanel(); // This is a hypothetical method that you should create for admin tasks.
        }
        else
        {
            DisplayCentered("Invalid username or password.");
            PauseForUser(); // Assuming you have a method to pause for user input
            isAdmin = false;
            Console.Clear();
        }
    }

    static void AdminPanel()
    {
        bool back = false;
        while (!back)
        {
            Console.Clear(); // Clear the console at the start of the loop.
            DisplayCentered("╔═════════════════════════╗");
            DisplayCentered("║       Admin Panel       ║");
            DisplayCentered("╚═════════════════════════╝\n");

            // Aligned menu options
            DisplayCentered("1. View All Users          ");
            DisplayCentered("2. Search Users            ");
            DisplayCentered("3. Modify User             ");
            DisplayCentered("4. Delete User             ");
            DisplayCentered("5. Back to Main Menu       ");

            DisplayCentered("\nEnter your choice: ", false); // Prompt to enter choice
            string adminChoice = Console.ReadLine();

            switch (adminChoice)
            {
                case "1":
                    Console.Clear();
                    ViewAllUsers();
                    PauseForUser();
                    break;
                case "2":
                    Console.Clear();
                    SearchUsers();
                    PauseForUser();
                    break;
                case "3":
                    Console.Clear();
                    ModifyUser();
                    PauseForUser();
                    break;
                case "4":
                    Console.Clear();
                    DeleteUser();
                    PauseForUser();
                    break;
                case "5":
                    back = true; // Will exit the loop and not display the admin panel again
                    break;
                default:
                    DisplayCentered("Invalid selection. Please choose again.");
                    PauseForUser();
                    break;
            }
        }
    }

    static void SearchUsers()
    {
        if (!isAdmin)
        {
            Console.WriteLine("Access denied.");
            return;
        }

        Console.Write("Enter the username you wish to search for: ");
        string usernameToSearch = Console.ReadLine();
        User foundUser = FindUserByUsername(usernameToSearch); // This method will now also print the user details if found.

        if (foundUser != null)
        {
            string filePath = @"C:\Users\chris\Desktop\MIDTERM CPE\Users\CSVSusers.csv"; // Path to your CSV

            // Check if the file exists
            if (!File.Exists(filePath))
            {
                Console.WriteLine("Error: File not found.");
                return;
            }

            // Read all lines from the CSV file
            string[] lines = File.ReadAllLines(filePath);

            // Search for the user in the CSV lines
            foreach (var line in lines)
            {
                string[] values = line.Split(',');

                if (values[0].Equals(usernameToSearch, StringComparison.OrdinalIgnoreCase))
                {
                    // Found the user in the CSV

                    // Print the tabular data header
                    Console.WriteLine("\n{0,-15} | {1,15} | {2,15} | {3,15}", "Username", "Bitcoin Balance", "Ethereum Balance", "Tether Balance");
                    Console.WriteLine(new string('-', 76)); // Creates a separator line

                    // Adjust the indices based on the actual structure of your CSV
                    string username = values[0];
                    string bitcoinBalance = values[2];
                    string ethereumBalance = values[3];
                    string tetherBalance = values[4];

                    Console.WriteLine("{0,-15} | {1,15} | {2,15} | {3,15}", username, bitcoinBalance, ethereumBalance, tetherBalance);
                    break; // Exit the loop once user is found and displayed.
                }
            }
        }
    }


    static User FindUserByUsername(string usernameToFind)
    {
        var user = users.FirstOrDefault(u => u.Username.Equals(usernameToFind, StringComparison.OrdinalIgnoreCase));

        if (user != null)
        {
            Console.WriteLine($"User found: {user.Username}");
            // Here you can print more details about the found user
        }
        else
        {
            Console.WriteLine("User not found.");
        }

        return user; // Return the found user or null if not found
    }

    static void DeleteUser()
    {
        // Check if the user is logged in as an admin before proceeding
        if (!isAdmin)
        {
            Console.WriteLine("Access denied. You must be an admin to perform this action.");
            PauseForUser(); // Pausing for better user experience
            return;
        }

        Console.Clear(); // Clears the screen for clarity

        // Display header
        Console.WriteLine(new string('═', 40));
        Console.WriteLine("║" + new string(' ', 13) + "Delete User" + new string(' ', 13) + "║");
        Console.WriteLine(new string('═', 40));
        Console.WriteLine();

        Console.Write("Enter the username of the user you want to delete: ");
        string usernameToDelete = Console.ReadLine();

        User userToDelete = FindUserByUsername(usernameToDelete);

        if (userToDelete != null)
        {
            // Remove user from the in-memory list
            users.Remove(userToDelete);

            // Save changes back to CSV
            SaveUsers();

            Console.WriteLine($"\nUser {userToDelete.Username} deleted successfully.");
        }
        else
        {
            Console.WriteLine("\nUser not found.");
        }

        PauseForUser(); // Pausing for better user experience
    }



    static void ModifyUser()
    {
        // Check if the user is logged in as an admin before proceeding
        if (!isAdmin)
        {
            Console.WriteLine("Access denied. You must be an admin to perform this action.");
            return;
        }

        Console.Clear(); // Clears the screen for a better visual experience

        // Display header
        Console.WriteLine(new string('═', 40));
        Console.WriteLine("║" + new string(' ', 14) + "Modify User" + new string(' ', 14) + "║");
        Console.WriteLine(new string('═', 40));
        Console.WriteLine();

        Console.Write("Enter the username of the user you want to modify: ");
        string usernameToModify = Console.ReadLine();

        User userToModify = FindUserByUsername(usernameToModify);

        if (userToModify != null)
        {
            Console.WriteLine($"\nUser {userToModify.Username} found.\n");
            Console.WriteLine("════════════════════════════════════════");
            Console.WriteLine("Enter new data. If you don't want to change a specific field, simply press Enter without typing anything.\n");

            // Get new username
            Console.Write("New Username (if needed): ");
            string newUsername = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(newUsername))
            {
                userToModify.Username = newUsername;
            }

            // Get new password
            Console.Write("\nNew Password (if needed): ");
            string newPassword = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                userToModify.SetPassword(newPassword); // Use the SetPassword method here
            }

            int index = users.IndexOf(userToModify);
            if (index != -1)
            {
                users[index] = userToModify;
            }

            SaveUsers(); // Save changes back to CSV
            Console.WriteLine("\n════════════════════════════════════════");
            Console.WriteLine("User data updated successfully.");
        }
        else
        {
            Console.WriteLine("\nUser not found.");
        }
        PauseForUser();
    }


    static void DisplayHelp()
    {
        Console.Clear();
        Console.WriteLine("╔═══════════════════════════════════════╗");
        Console.WriteLine("║                HELP                   ║");
        Console.WriteLine("╚═══════════════════════════════════════╝");
        Console.WriteLine("1. Register: Allows you to create a new account.");
        Console.WriteLine("2. Login: Login to your existing account to manage funds.");
        Console.WriteLine("3. Help: This guide.");
        Console.WriteLine("4. Exit: Closes the application.");
        Console.WriteLine();
    }

    static void DisplayHeader()
    {
        int consoleWidth = Console.WindowWidth;

        string[] lines = {
        "",
        " ▄████████    ▄████████ ▄██   ▄      ▄███████▄     ███        ▄████████ ▀████    ▐████▀ ",
        "███    ███   ███    ███ ███   ██▄   ███    ███ ▀█████████▄   ███    ███   ███▌   ████▀  ",
        "███    █▀    ███    ███ ███▄▄▄███   ███    ███    ▀███▀▀██   ███    █▀     ███  ▐███    ",
        "███         ▄███▄▄▄▄██▀ ▀▀▀▀▀▀███   ███    ███     ███   ▀  ▄███▄▄▄        ▀███▄███▀    ",
        "███        ▀▀███▀▀▀▀▀   ▄██   ███ ▀█████████▀      ███     ▀▀███▀▀▀        ████▀██▄     ",
        "███    █▄  ▀███████████ ███   ███   ███            ███       ███    █▄    ▐███  ▀███    ",
        "███    ███   ███    ███ ███   ███   ███            ███       ███    ███  ▄███     ███▄  ",
        "████████▀    ███    ███  ▀█████▀   ▄████▀         ▄████▀     ██████████ ████       ███▄ ",
        "             ███    ███                                                                 ",
        "           Cryptocurrency Banking Portal           "
    };

        foreach (string line in lines)
        {
            Console.WriteLine(line.PadLeft((consoleWidth + line.Length) / 2));
            Thread.Sleep(100); // Delay of 100 milliseconds
        }
    }



    static void DisplayMenu()
    {
        int consoleWidth = Console.WindowWidth;
        Console.WriteLine();

        string[] menuItems = {
        "1. Register",
        "2. User Login",
        "3. Admin Login",
        "4. Help",
        "5. Exit"
    };

        int maxLength = menuItems.Max(item => item.Length);

        foreach (string menuItem in menuItems)
        {
            int padding = (consoleWidth - maxLength) / 2;

            Console.WriteLine(menuItem.PadLeft(menuItem.Length + padding));
        }
    }



    static void DisplayBankingOptionsHelp()
    {
        Console.Clear();
        Console.WriteLine("╔═══════════════════════════════════════╗");
        Console.WriteLine("║          BANKING OPTIONS HELP         ║");
        Console.WriteLine("╚═══════════════════════════════════════╝");
        Console.WriteLine("");
        Console.WriteLine("");

        Console.WriteLine("1. View Balance - Check the current balance of your Bitcoin and Ethereum accounts.");
        Console.WriteLine("2. Deposit - Add funds to your Bitcoin or Ethereum accounts.");
        Console.WriteLine("3. Withdraw - Remove funds from your Bitcoin or Ethereum accounts.");
        Console.WriteLine("4. Logout - Exit the banking menu and return to the main menu.");
        Console.WriteLine("5. Help - Display this help guide.");

        PauseForUser();
        Console.Clear();
    }

    static void Register()
    {
        Console.Clear();
        int consoleWidth = Console.WindowWidth;

        // Centered header
        string[] header = {
        "╔═══════════════════════════════════════╗",
        "║              REGISTRATION             ║",
        "╚═══════════════════════════════════════╝"
    };

        foreach (string line in header)
        {
            Console.WriteLine(line.PadLeft((consoleWidth + line.Length) / 2));
        }

        Console.WriteLine();  // Just for a cleaner spacing

        // Centered prompts
        Console.Write("Username: ".PadLeft(consoleWidth / 2));
        var username = Console.ReadLine();

        // Check if user already exists
        if (users.Exists(user => user.Username == username))
        {
            Console.WriteLine("User already exists. Choose another username.".PadLeft((consoleWidth + "User already exists. Choose another username.".Length) / 2));
            Console.ReadKey();  // Replaces PauseForUser, waits for a single key press
            return;
        }


        Console.Write("Password: ".PadLeft(consoleWidth / 2));
        var password = ReadPassword(); // Use the masking function here

        currentUser = new User { Username = username };
        currentUser.SetPassword(password);
        users.Add(currentUser);

        // Attempt to save users and catch any potential errors.
        try
        {
            SaveUsers();
            Console.WriteLine("Registration Successful!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving user: {ex.Message}");
        }
    }
    public static string ReadPassword(char maskChar = '*')
    {
        StringBuilder password = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);
            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b"); // Move the cursor back and overwrite the last *
                }
            }
            else
            {
                password.Append(keyInfo.KeyChar);
                Console.Write(maskChar);
            }
        }
        return password.ToString();
    }

    static void DisplayCentered(string message, bool newlineAfter = true)
    {
        int consoleWidth = Console.WindowWidth;
        int centeredPadding = (consoleWidth - message.Length) / 2;
        Console.Write(new string(' ', centeredPadding));
        Console.Write(message);
        if (newlineAfter)
        {
            Console.WriteLine();
        }
    }

    static void Login()
    {
        Console.Clear();
        DisplayCentered("╔═══════════════════════════════════════╗");
        DisplayCentered("║                 LOGIN                 ║");
        DisplayCentered("╚═══════════════════════════════════════╝");
        DisplayCentered("");
        DisplayCentered("Username: ", false);
        var username = Console.ReadLine();
        DisplayCentered("Password: ", false);
        var password = ReadPassword();

        currentUser = users.Find(user => user.Username == username && user.Authenticate(password));

        if (currentUser != null)
        {
            Console.Clear();
            bool loggedIn = true;
            while (loggedIn)
            {
                Console.WriteLine();
                DisplayCentered("╔═══════════════════════════════════════╗");
                DisplayCentered("║             BANKING OPTIONS           ║");
                DisplayCentered("╚═══════════════════════════════════════╝");
                DisplayCentered("1. View Balance             ");
                DisplayCentered("2. Deposit                  ");
                DisplayCentered("3. Withdraw                 ");
                DisplayCentered("4. Help                     ");
                DisplayCentered("5. Exchange Currency        ");
                DisplayCentered("6. Logout                   ");

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        currentUser.DisplayBalances();
                        PauseForUser();
                        Console.Clear();
                        break;
                    case "2":
                        Deposit();
                        PauseForUser();
                        Console.Clear();
                        break;
                    case "3":
                        Withdraw();
                        PauseForUser();
                        Console.Clear();
                        break;
                    case "4":
                        DisplayBankingOptionsHelp();
                        PauseForUser();
                        Console.Clear();
                        break;
                    case "5":
                        ExchangeCurrency();
                        PauseForUser();
                        Console.Clear();
                        break;
                    case "6":
                        loggedIn = false;
                        break;
                    default:
                        DisplayCentered("Invalid choice. Please select a valid option.");
                        PauseForUser();
                        Console.Clear();
                        break;
                }
            }
        }
        else
        {
            DisplayCentered("Invalid login.");
            PauseForUser();
            Console.Clear();
        }
    }
    static void AdminLoginNew()
    {
        Console.Write("Enter admin username: ");
        string username = Console.ReadLine();
        Console.Write("Enter password: ");
        string password = Console.ReadLine();


        currentUser = users.FirstOrDefault(u => u.Username == username && u.ValidatePassword(password) && u.IsAdmin);

        if (currentUser != null && currentUser.IsAdmin)
        {
            Console.WriteLine($"Welcome {currentUser.Username}!");
            AdminPanel(); // Open admin panel after successful login.
        }
        else
        {
            Console.WriteLine("Invalid login. Please check your username and password.");
        }
    }


    static void AdminPanelNew()
    {
        bool adminExit = false;
        while (!adminExit)
        {
            Console.WriteLine("╔══════════════════════════════╗");
            Console.WriteLine("║       ADMIN DASHBOARD        ║");
            Console.WriteLine("╚══════════════════════════════╝");
            Console.WriteLine("1. View All Users");
            Console.WriteLine("2. Modify User");
            Console.WriteLine("3. Delete User");
            Console.WriteLine("4. Logout");

            Console.Write("Choice: ");
            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    ViewAllUsers(); // Method to display all users
                    break;
                case "2":
                    ModifyUser(); // Method to modify user details
                    break;
                case "3":
                    DeleteUser(); // Method to delete a user
                    break;
                case "4":
                    adminExit = true;
                    break;
                default:
                    Console.WriteLine("Invalid choice. Please select a valid option.");
                    break;
            }

            SaveUsers();
        }
    }
    static void Deposit()
    {
        DisplayCentered("╔═════════════════════╗");
        DisplayCentered("║ 1. Bitcoin          ║");
        DisplayCentered("║ 2. Ethereum         ║");
        DisplayCentered("║ 3. USDT             ║");
        DisplayCentered("╚═════════════════════╝");

        var choice = Console.ReadLine();

        Console.WriteLine($"Note: A banking fee of {User.FeeConstants.BankingFee * 100}% will be applied to your deposit.");
        Console.Write("Amount: ");
        double amount;
        if (!double.TryParse(Console.ReadLine(), out amount))
        {
            Console.WriteLine("Invalid amount entered. Please try again.");
            return;
        }

        double fee = amount * User.FeeConstants.BankingFee;
        double amountAfterFee = amount - fee;

        switch (choice)
        {
            case "1":
                currentUser.Deposit(currentUser.BitcoinWallet, amountAfterFee);
                Console.WriteLine($"Deposited {amountAfterFee} Bitcoin. Fee: {fee} Bitcoin.");
                break;
            case "2":
                currentUser.Deposit(currentUser.EthereumWallet, amountAfterFee);
                Console.WriteLine($"Deposited {amountAfterFee} Ethereum. Fee: {fee} Ethereum.");
                break;
            case "3":
                currentUser.Deposit(currentUser.TetherWallet, amountAfterFee);
                Console.WriteLine($"Deposited {amountAfterFee} Tether. Fee: {fee} Tether.");
                break;
            default:
                Console.WriteLine("Invalid choice. Please select a valid cryptocurrency.");
                break;
        }

        SaveUsers();
    }


    static void Withdraw()
    {
        DisplayCentered("╔═════════════════════╗");
        DisplayCentered("║ 1. Bitcoin          ║");
        DisplayCentered("║ 2. Ethereum         ║");
        DisplayCentered("║ 3. Tether           ║");
        DisplayCentered("╚═════════════════════╝");

        var choice = Console.ReadLine();

        Console.WriteLine($"Note: A banking fee of {User.FeeConstants.BankingFee * 100}% will be applied to your withdrawal.");
        Console.Write("Amount: ");
        double amount;
        if (!double.TryParse(Console.ReadLine(), out amount))
        {
            Console.WriteLine("Invalid amount entered. Please try again.");
            return;
        }

        double fee = amount * User.FeeConstants.BankingFee;
        double amountAfterFee = amount - fee;
        string cryptoName = "";
        CryptoType cryptoType;
        bool withdrawalSuccessful = false;

        switch (choice)
        {
            case "1":
                cryptoName = "Bitcoin";
                cryptoType = CryptoType.BTC;
                if (currentUser.BitcoinWallet.Balance >= amount)
                {
                    currentUser.Withdraw(currentUser.BitcoinWallet, amountAfterFee);
                    withdrawalSuccessful = true;
                }
                break;
            case "2":
                cryptoName = "Ethereum";
                cryptoType = CryptoType.ETH;
                if (currentUser.EthereumWallet.Balance >= amount)
                {
                    currentUser.Withdraw(currentUser.EthereumWallet, amountAfterFee);
                    withdrawalSuccessful = true;
                }
                break;
            case "3":
                cryptoName = "Tether";
                cryptoType = CryptoType.USDT;
                if (currentUser.TetherWallet.Balance >= amount)
                {
                    currentUser.Withdraw(currentUser.TetherWallet, amountAfterFee);
                    withdrawalSuccessful = true;
                }
                break;
            default:
                Console.WriteLine("Invalid choice. Please select a valid cryptocurrency.");
                return;
        }

        if (withdrawalSuccessful)
        {
            PrintReceipt(currentUser, cryptoName, cryptoType, amount, fee, amountAfterFee);
            SaveUsers(); // Moved inside this if block
        }
        else
        {
            Console.WriteLine($"Insufficient balance for withdrawal and fee in your {cryptoName} account.");
        }
    }

    static void PrintReceipt(User user, string cryptoName, CryptoType cryptoType, double amount, double fee, double amountAfterFee)
    {
        Console.Clear();

        DisplayCentered("╔═════════════════════════════════════╗");
        DisplayCentered("║           DIGITAL RECEIPT           ║");
        DisplayCentered("╚═════════════════════════════════════╝");
        DisplayCentered($"Username: {user.Username}");
        DisplayCentered($"Cryptocurrency: {cryptoName}");
        DisplayCentered($"Amount Requested: {amount} {cryptoName}");
        DisplayCentered($"Banking Fee: {fee} {cryptoName}");
        DisplayCentered($"Amount Withdrawn After Fee: {amountAfterFee} {cryptoName}");
        DisplayCentered($"New Balance: {user.GetBalance(cryptoType)} {cryptoName}");
        DisplayCentered("\nThank you for using our service!");
    }



    static void ExchangeCurrency()
    {
        Console.WriteLine("╔═════════════════════════╗");
        Console.WriteLine("║ Cryptocurrency Exchange ║");
        Console.WriteLine("╚═════════════════════════╝");

        Console.WriteLine("Choose the currency to exchange FROM:");
        Console.WriteLine("1. BTC\n2. ETH\n3. USDT");
        var fromChoice = Console.ReadLine();

        Console.WriteLine("Choose the currency to exchange TO:");
        Console.WriteLine("1. BTC\n2. ETH\n3. USDT");
        var toChoice = Console.ReadLine();

        if (fromChoice == toChoice)
        {
            Console.WriteLine("You cannot exchange the same currency for itself. Please choose different currencies.");
            return;
        }

        Console.Write("Amount to exchange: ");
        double amount;
        if (!double.TryParse(Console.ReadLine(), out amount) || amount <= 0)
        {
            Console.WriteLine("Invalid amount entered. Please enter a positive value.");
            return;
        }

        User.CryptoType from = StringToCryptoType(fromChoice);
        User.CryptoType to = StringToCryptoType(toChoice);

        // Check if the user has enough balance to perform the exchange
        double currentBalance = currentUser.GetBalance(from);
        if (currentBalance < amount)
        {
            Console.WriteLine($"Insufficient {from} balance.");
            return;
        }

        double exchangeRate = CryptoOperations.GetConversionRate(from, to);
        double fee = 0.005; // 0.5% exchange fee
        double receivedAmount = amount * exchangeRate * (1 - fee);

        // Deducting from the source wallet
        currentUser.UpdateWalletBalance(from, -amount); // Negative because we are deducting

        // Adding to the target wallet
        currentUser.UpdateWalletBalance(to, receivedAmount);

        Console.WriteLine($"Successfully exchanged {amount} {from} to {receivedAmount} {to} with a {fee * 100}% fee.");
    }

    private static User.CryptoType StringToCryptoType(string choice)
    {
        switch (choice)
        {
            case "1": return User.CryptoType.BTC;
            case "2": return User.CryptoType.ETH;
            case "3": return User.CryptoType.USDT;
            default: throw new ArgumentException("Invalid crypto choice.");
        }
    }
}