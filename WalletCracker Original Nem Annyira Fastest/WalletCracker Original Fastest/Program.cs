using System;
using System.Collections.Concurrent; // For ConcurrentBag
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Threading;

class Program
{
    static int Sessions = 0;
    private static readonly HttpClient httpClient = new HttpClient();
    private static ConcurrentBag<WalletInfo> wallets = new ConcurrentBag<WalletInfo>();

    static int ParallelTasks = 2;
    static int BalanceCheck = 425;

    public class WalletInfo
    {
        public string Mnemonic { get; set; }
        public string Address { get; set; }
        public decimal Balance { get; set; }
        public bool BalanceChecked { get; set; } = false;
    }

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
        while (true)
        {
            await Task.Delay(10); // Várakozás

            var tasksList = new List<Task>();
            var walletsToAdd = new List<WalletInfo>();

            Parallel.For(0, tasks, i =>
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
                    Balance = 0
                };

                lock (wallets)
                {
                    walletsToAdd.Add(wallet);
                }
            });

            await Task.WhenAll(tasksList);

            lock (wallets)
            {
                foreach (var wallet in walletsToAdd)
                {
                    wallets.Add(wallet);
                }
            }
        }
    }

    static async Task QueryBalancesAsync(int balanceCheck)
    {
        while (true)
        {
            IEnumerable<WalletInfo> walletsToCheck = wallets.Where(w => !w.BalanceChecked).ToList();

            for (int i = 0; i < walletsToCheck.Count(); i += balanceCheck)
            {
                var batch = walletsToCheck.Skip(i).Take(balanceCheck);
                string addresses = string.Join("|", batch.Select(w => w.Address));

                try
                {
                    string apiUrl = $"https://blockchain.info/balance?active={addresses}";
                    string json = await httpClient.GetStringAsync(apiUrl);
                    JObject data = JObject.Parse(json);

                    foreach (var wallet in batch)
                    {
                        decimal balance = (decimal)data[wallet.Address]["final_balance"] / 100000000;
                        wallet.Balance = balance;
                        wallet.BalanceChecked = true;
                        Sessions++;
                        Console.WriteLine($"Checked Wallets: {Sessions} | Balance: {balance} BTC | Mnemonic: {wallet.Mnemonic}");

                        if (balance > 0)
                        {
                            string walletInfo = $"Checked Wallets: {Sessions} | Balance: {balance} BTC | Mnemonic: {wallet.Mnemonic} | Address: {wallet.Address}\n";
                            using (StreamWriter writer = new StreamWriter("wallets_with_balance.txt", append: true))
                            {
                                await writer.WriteLineAsync(walletInfo);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error querying balance: {e.Message}");
                    // Implement retry logic based on specific conditions
                }
                await Task.Delay(50); // Example rate limiting, adjust based on API feedback
            }
        }
    }
}
