using System;

namespace Mahjong.CoreTests
{
    /// <summary>離線執行規則引擎測試的進入點。失敗時回傳非 0，方便之後接 CI。</summary>
    static class Program
    {
        static int Main()
        {
            try
            {
                return Tests.MahjongTests.RunAll() ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"測試執行時發生未預期例外：{ex.GetType().Name} - {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 2;
            }
        }
    }
}
