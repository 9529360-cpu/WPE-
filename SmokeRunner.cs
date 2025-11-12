using System;
using System.Threading.Tasks;
using 甯佸畨閲忓寲鏈哄櫒浜?Services;
using 甯佸畨閲忓寲鏈哄櫒浜?Services.AI;

class Runner
{
    public static async Task<int> Main()
    {
        try
        {
            var coord = ServiceLocator.GetAICentralCoordinator();
            await coord.StartAsync();

            await ServiceLocator.AutoTrader.StartAsync(new[] { "BTCUSDT", "ETHUSDT" }, AccountType.Simulated);

            await Task.Delay(TimeSpan.FromSeconds(60));

            await ServiceLocator.AutoTrader.StopAsync();
            await coord.StopAsync();

            Console.WriteLine("Smoke test completed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex}");
            return 1;
        }
    }
}
