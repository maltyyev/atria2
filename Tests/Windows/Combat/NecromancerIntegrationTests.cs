namespace Tests.Windows.Combat;

/// <summary>Runs the Windows-only presentation and execution checks around portable scenario coverage.</summary>
internal static class NecromancerIntegrationTests
{
    internal static string Run(string root)
    {
        Console.WriteLine(NarrativeRevisionTests.Run(root));
        return Tests.Core.Combat.NecromancerTests.Run(root, state =>
        {
            NecromancerPresentationTests.Run(root, state);
            NecromancerExecutionTests.Run(root);
        });
    }
}
