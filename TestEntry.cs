// نقطة دخول الاختبارات - منفصلة حتى /main: يقدر يختارها بدل Main تبع البرنامج
namespace NetSpeedMeter
{
    static class TestEntry
    {
        static int Main()
        {
            return Tests.Run();
        }
    }
}
