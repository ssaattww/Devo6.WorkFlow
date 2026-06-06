namespace Devo6.WorkFlow.Abstractions;

public interface IStep<TOut>
{
    TOut Execute(StepInput input);
}
