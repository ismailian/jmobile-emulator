using JMobile.Tests;

Console.WriteLine();
Console.WriteLine("  JMobile test suite");

VmConformanceTests.Register();
CoreUnitTests.Register();
GraphicsUnitTests.Register();
SuiteUnitTests.Register();
SessionSmokeTests.Register();

// Only a bare word is a filter; anything starting with a dash belongs to the launcher, not to us.
string? filter = args.FirstOrDefault(argument => !argument.StartsWith('-'));
return Test.RunAll(filter);
