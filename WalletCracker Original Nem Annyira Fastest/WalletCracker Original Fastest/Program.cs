using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;

class Program
{
    static int Sessions = 0;

    public class WalletInfo
    {
        public string Mnemonic { get; set; }
        public string Address { get; set; }
        public decimal Balance { get; set; }
        public bool BalanceChecked { get; set; } = false; // Ez jelzi, hogy a pénztárca egyenlege már le lett-e kérdezve
    }

    private static readonly HttpClient httpClient = new HttpClient();
    private static List<WalletInfo> wallets = new List<WalletInfo>();

    static int ParallelTasks = 1;
    static int BalanceCheck = 425;

    static async Task Main(string[] args)
    {
        while (true)
        {
            Task walletGenerationTask = GenerateWalletsAsync(ParallelTasks);
            Task balanceQueryTask = QueryBalancesAsync(BalanceCheck);
            await Task.WhenAll(walletGenerationTask, balanceQueryTask);
        }
    }

    static async Task GenerateWalletsAsync(int tasks)
    {
        await Task.Run(async () =>
        {
            while (true) // Végtelen ciklus a folyamatos generáláshoz
            {
                // Limiting the number of parallel tasks to avoid overloading the thread pool
                var tasksList = new List<Task>();
                for (int i = 0; i < tasks; i++)
                {
                    tasksList.Add(Task.Run(() =>
                    {
                        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
                        ExtKey extendedKey = mnemonic.DeriveExtKey();
                        Key privateKey = extendedKey.PrivateKey;
                        var publicKey = privateKey.PubKey;
                        var address = publicKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main).ToString();
                        var wallet = new WalletInfo
                        {
                            Mnemonic = mnemonic.ToString(),
                            Address = address,
                            Balance = 0 // Kezdeti egyenleg beállítása 0-ra
                        };

                        lock (wallets)
                        {
                            wallets.Add(wallet);
                        }
                    }));
                }
                await Task.WhenAll(tasksList);
                // Optionally, add a delay or a mechanism to pause/resume to manage workload and CPU usage
            }
        });
    }

    static async Task QueryBalancesAsync(int BalanceCheck)
    {
        while (true)
        {
            List<WalletInfo> walletsToCheck;
            lock (wallets)
            {
                // Kiválasztjuk csak azokat a pénztárcákat, amelyek egyenlegét még nem ellenőriztük
                walletsToCheck = wallets.Where(w => !w.BalanceChecked).ToList();
            }

            for (int i = 0; i < walletsToCheck.Count; i += BalanceCheck)
            {
                var batch = walletsToCheck.Skip(i).Take(BalanceCheck);
                string addresses = string.Join("|", batch.Select(w => w.Address));

                try
                {
                    string apiUrl = $"https://blockchain.info/balance?active={addresses}";
                    string json = await httpClient.GetStringAsync(apiUrl);
                    dynamic data = JObject.Parse(json);

                    foreach (var wallet in batch)
                    {
                        decimal balance = (decimal)data[wallet.Address]["final_balance"] / 100000000; // Convert from satoshis to BTC
                        wallet.Balance = balance;
                        wallet.BalanceChecked = true; // Jelöljük, hogy le lett kérdezve az egyenleg
                        Sessions++;
                        Console.WriteLine($"Checked Wallets: {Sessions} | Balance: {balance} BTC | Mnemonic: {wallet.Mnemonic}");

                        if (balance > 0)
                        {
                            string walletInfo = $"Checked Wallets: {Sessions} | Balance: {balance} BTC | Mnemonic: {wallet.Mnemonic} | Address: {wallet.Address}\n";
                            using (StreamWriter writer = File.AppendText("wallets_with_balance.txt"))
                            {
                                await writer.WriteLineAsync(walletInfo);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error querying balance: {e.Message}");
                }
                await Task.Delay(50); // Rate limiting miatt késleltetés
            }
        }
    }
}