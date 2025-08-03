using System.Collections;
using System.Diagnostics;
using Respsody.Generators.Library;

namespace Respsody.Generators.Syntax;

internal class CommandNodeExplication(CommandSyntaxNode command)
{
    public IEnumerable<Explication> Traverse()
    {
        var backtracking = new Stack<Variations>();
        backtracking.Push(new Variations([[command]], [], []));

        while (backtracking.Count > 0)
        {
            var variations = backtracking.Peek();
            var (examinationQueue, explication) = variations.Next();

            if (!variations.Any)
                backtracking.Pop();

            while (examinationQueue.Count > 0)
            {
                var nodeToExamine = examinationQueue.Dequeue();

                if (NeedToAdd(nodeToExamine))
                    explication.Add(nodeToExamine);

                var variationsOfChildren = nodeToExamine.GetPossibleSequencesOfChildren().ToArray();

                if (variationsOfChildren.Length == 0)
                    continue;

                if (variationsOfChildren.Length > 1)
                    backtracking.Push(
                        new Variations(
                            variationsOfChildren.Skip(1),
                            [.. explication],
                            new Queue<SyntaxNode>(examinationQueue)));

                foreach (var childNode in variationsOfChildren[0])
                    examinationQueue.Enqueue(childNode);
            }

            yield return new Explication(explication);
        }
    }

    public IEnumerable<Explication> TraverseWithStack()
    {
        var backtracking = new Stack<VariationsWithStack>();
        backtracking.Push(new VariationsWithStack([[command]]));

        while (backtracking.Count > 0)
        {
            var variations = backtracking.Peek();
            var (examinationStack, explication) = variations.Next();

            if (!variations.Any)
                backtracking.Pop();

            while (examinationStack.TryPop(out var node))
            {
                if (NeedToAdd(node))
                {
                    if (node is ArraySyntaxNode paramsSyntaxNode)
                    {
                        backtracking.Push(
                            new VariationsWithStack(
                                [],
                                [.. explication],
                                examinationStack.Clone()));

                        paramsSyntaxNode.TryRemoveExplicitArguments(explication);
                    }

                    explication.Add(node);
                }

                var variationsOfChildren = node.GetPossibleSequencesOfChildren().ToArray();

                if (variationsOfChildren.Length == 0)
                    continue;

                if (variationsOfChildren.Length > 1)
                    backtracking.Push(
                        new VariationsWithStack(
                            variationsOfChildren.Skip(1),
                            [.. explication],
                            examinationStack.Clone()));

                foreach (var childNode in variationsOfChildren[0].Reverse())
                    examinationStack.Push(childNode);
            }

            yield return new Explication(explication);
        }
    }

    private static bool NeedToAdd(SyntaxNode node)
        => node is CommandSyntaxNode or ParameterSyntaxNode
            or OptionSyntaxNode or KeySyntaxNode or ArraySyntaxNode;

    [DebuggerDisplay("{ToDebugString()}")]
    public class Explication(IEnumerable<SyntaxNode> nodes) : IEnumerable<SyntaxNode>
    {
        private readonly List<SyntaxNode> _nodes = nodes.ToList();

        public IEnumerator<SyntaxNode> GetEnumerator()
            => _nodes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _nodes.GetEnumerator();

        public string ToDebugString()
            => string.Join(" ", _nodes.Select(n => n.ToDisplayString()));

        private bool Equals(Explication other)
        {
            return _nodes.Count == other._nodes.Count
                   && _nodes.Zip(_nodes, (a, b) => a.Equals(b)).All(t => t);
        }

        public override bool Equals(object? obj)
        {
            if (obj is null)
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != GetType())
                return false;
            return Equals((Explication)obj);
        }

        public override int GetHashCode()
        {
            return _nodes.UncheckedSum(n => n.GetHashCode());
        }
    }

    public class ExaminationStack
    {
        private SyntaxNode[] _nodes = [];
        private int _position = -1;

        public void Push(SyntaxNode node)
        {
            var nextPosition = ++_position;
            if (nextPosition == _nodes.Length)
                Array.Resize(ref _nodes, Math.Max(2, _nodes.Length * 2));

            _nodes[_position] = node;
        }

        public ExaminationStack Clone()
            => new()
            {
                _nodes = [.._nodes],
                _position = _position
            };

        public bool TryPop(out SyntaxNode node)
        {
            if (_position < 0)
            {
                node = default!;
                return false;
            }

            node = _nodes[_position--];
            return true;
        }
    }

    private class Variations
    {
        private readonly SyntaxNode[] _explication;
        private readonly Queue<IEnumerable<SyntaxNode>> _queue = new();
        private readonly Queue<SyntaxNode> _queueSnapshot;

        public bool Any => _queue.Count > 0;

        public Variations(
            IEnumerable<IEnumerable<SyntaxNode>> variations,
            SyntaxNode[] explication,
            Queue<SyntaxNode> queueSnapshot)
        {
            _explication = explication;
            _queueSnapshot = queueSnapshot;
            foreach (var node in variations)
                _queue.Enqueue(node);
        }

        public (Queue<SyntaxNode> ExaminationQueue, List<SyntaxNode> Explication) Next()
        {
            var examinationQueue = new Queue<SyntaxNode>(_queueSnapshot);
            foreach (var node in _queue.Dequeue())
                examinationQueue.Enqueue(node);

            var explication = new List<SyntaxNode>(_explication);
            return (examinationQueue, explication);
        }
    }

    private class VariationsWithStack
    {
        private readonly SyntaxNode[] _explication;
        private readonly Queue<IEnumerable<SyntaxNode>> _queue = new();
        private readonly ExaminationStack _stackSnapshot;

        public bool Any => _queue.Count > 0;

        public VariationsWithStack(
            IEnumerable<IEnumerable<SyntaxNode>> variations,
            SyntaxNode[]? explication = null,
            ExaminationStack? stackSnapshot = null)
        {
            _explication = explication ?? [];
            _stackSnapshot = stackSnapshot ?? new ExaminationStack();
            foreach (var node in variations)
                _queue.Enqueue(node);
        }

        public (ExaminationStack Examination, List<SyntaxNode> Explication) Next()
        {
            var examinationQueue = _stackSnapshot.Clone();
            //new Stack<SyntaxNode>(_stackSnapshot.Reverse());

            if (_queue.Count > 0)
                foreach (var node in _queue.Dequeue().Reverse())
                    examinationQueue.Push(node);

            var explication = new List<SyntaxNode>(_explication);
            return (examinationQueue, explication);
        }
    }
}