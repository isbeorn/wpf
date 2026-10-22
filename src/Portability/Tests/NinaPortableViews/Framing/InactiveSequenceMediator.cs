using System.Reflection;
using NINA.Sequencer.Interfaces.Mediator;

namespace FixtureServices;

public class InactiveSequenceMediator : DispatchProxy
{
    public static ISequenceMediator Create() => DispatchProxy.Create<ISequenceMediator, InactiveSequenceMediator>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.DeclaringType == typeof(ISequenceMediator) && targetMethod.Name == "get_Initialized") return false;
        throw new InvalidOperationException("FIXTURE_SEQUENCER_FORBIDDEN: " + targetMethod?.Name);
    }
}
