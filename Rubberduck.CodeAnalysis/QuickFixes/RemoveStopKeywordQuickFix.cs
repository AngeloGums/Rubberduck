using Rubberduck.Inspections.Abstract;
using Rubberduck.Inspections.Concrete;
using Rubberduck.Parsing.Inspections.Abstract;
using Rubberduck.Parsing.Rewriter;

namespace Rubberduck.Inspections.QuickFixes
{
    /// <summary>
    /// Removes 'Stop' instruction.
    /// </summary>
    /// <inspections>
    /// <inspection name="StopKeywordInspection" />
    /// </inspections>
    /// <canfix procedure="false" module="false" project="false" />
    /// <example>
    /// <before>
    /// <![CDATA[
    /// Option Explicit
    /// 
    /// Public Sub DoSomething()
    ///     MsgBox "Hi"
    ///     Stop
    /// End Sub
    /// ]]>
    /// </before>
    /// <after>
    /// <![CDATA[
    /// Option Explicit
    /// 
    /// Public Sub DoSomething()
    ///     MsgBox "Hi"
    /// 
    /// End Sub
    /// ]]>
    /// </after>
    /// </example>
    public sealed class RemoveStopKeywordQuickFix : QuickFixBase
    {
        public RemoveStopKeywordQuickFix()
            : base(typeof(StopKeywordInspection))
        {}

        public override void Fix(IInspectionResult result, IRewriteSession rewriteSession)
        {
            var rewriter = rewriteSession.CheckOutModuleRewriter(result.QualifiedSelection.QualifiedName);
            rewriter.Remove(result.Context);
        }

        public override string Description(IInspectionResult result) => Resources.Inspections.QuickFixes.RemoveStopKeywordQuickFix;

        public override bool CanFixInProcedure => false;
        public override bool CanFixInModule => false;
        public override bool CanFixInProject => false;
    }
}