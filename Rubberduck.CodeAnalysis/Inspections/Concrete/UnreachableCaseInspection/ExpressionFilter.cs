﻿using Rubberduck.Parsing.Grammar;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rubberduck.Inspections.Concrete.UnreachableCaseInspection
{
    public enum VariableClauseTypes
    {
        Predicate,
        Value,
        Range,
        Is
    };

    public interface IExpressionFilter
    {
        void AddExpression(IRangeClauseExpression expression);
        void AddComparablePredicateFilter(string variable, string variableTypeName);
        bool HasFilters { get; }
        bool FiltersAllValues { get; }
        IParseTreeValue SelectExpressionValue { set; get; }
    }

    public class ExpressionFilter<T> : IExpressionFilter where T : IComparable<T>
    {
        private struct PredicateValueExpression
        {
            private readonly int _hashCode;
            private readonly string _toString;

            public string LHS { private set; get; }
            public T RHS { private set; get; }
            public string OpSymbol { private set; get; }

            public PredicateValueExpression(string lhs, T rhs, string opSymbol)
            {
                LHS = lhs;
                RHS = rhs;
                OpSymbol = opSymbol;
                _toString = $"{LHS} {OpSymbol} {RHS}";
                _hashCode = _toString.GetHashCode();
            }

            public override string ToString() => _toString;
            public override int GetHashCode() => _hashCode;
            public override bool Equals(object obj)
            {
                if (!(obj is PredicateValueExpression expression))
                {
                    return false;
                }
                return _toString.Equals(expression.ToString());
            }
        }

        private readonly T _trueValue;
        private readonly T _falseValue;
        private readonly string _filterTypeName;
        private readonly int _hashCode;
        private string _toString;
        protected IParseTreeValue _selectExpressionValue;

        public ExpressionFilter(StringToValueConversion<T> converter, string typeName)
        {
            Converter = converter;
            _filterTypeName = typeName;
            _hashCode = _filterTypeName.GetHashCode();
            converter(Tokens.True, out _trueValue, typeName);
            converter(Tokens.False, out _falseValue, typeName);
            _selectExpressionValue = null;
        }

        private HashSet<LikeExpression> LikePredicates { get; } = new HashSet<LikeExpression>();

        private HashSet<PredicateValueExpression> ComparablePredicates { get; } = new HashSet<PredicateValueExpression>();

        private bool IsDirty { set; get; } = true;

        protected Dictionary<VariableClauseTypes, HashSet<string>> Variables { get; } = new Dictionary<VariableClauseTypes, HashSet<string>>()
        {
            [VariableClauseTypes.Is] = new HashSet<string>(),
            [VariableClauseTypes.Predicate] = new HashSet<string>(),
            [VariableClauseTypes.Range] = new HashSet<string>(),
            [VariableClauseTypes.Value] = new HashSet<string>(),
        };

        protected StringToValueConversion<T> Converter { set; get; } = null;

        protected HashSet<T> SingleValues { set; get; } = new HashSet<T>();

        protected HashSet<RangeOfValues> Ranges { set; get; } = new HashSet<RangeOfValues>();

        protected FilterLimits<T> Limits { get; } = new FilterLimits<T>();

        private Dictionary<string, IExpressionFilter> ComparablePredicateFilters { set; get; } = new Dictionary<string, IExpressionFilter>();

        private Dictionary<string, IExpressionFilter> ComparablePredicateFiltersInverse { set; get; } = new Dictionary<string, IExpressionFilter>();

        public void AddComparablePredicateFilter(string variable, string variableTypeName)
        {
            if (variable is null || variable.Length == 0 || variableTypeName is null || variableTypeName.Length == 0)
            {
                return;
            }

            if (!ComparablePredicateFilters.ContainsKey(variable))
            {
                ComparablePredicateFilters.Add(variable, ExpressionFilterFactory.Create(variableTypeName));
                ComparablePredicateFiltersInverse.Add(variable, ExpressionFilterFactory.Create(variableTypeName));
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is ExpressionFilter<T> filter))
            {
                return false;
            }

            return Ranges.SetEquals(filter.Ranges)
                && SingleValues.SetEquals(filter.SingleValues)
                && ComparablePredicates.SetEquals(filter.ComparablePredicates)
                && LikePredicates.SetEquals(filter.LikePredicates)
                && this[VariableClauseTypes.Range].SetEquals(filter[VariableClauseTypes.Range])
                && this[VariableClauseTypes.Value].SetEquals(filter[VariableClauseTypes.Value])
                && this[VariableClauseTypes.Predicate].SetEquals(filter[VariableClauseTypes.Predicate])
                && this[VariableClauseTypes.Is].SetEquals(filter[VariableClauseTypes.Is])
                && Limits.Equals(filter.Limits);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override string ToString()
        {
            if (!IsDirty && _toString != null)
            {
                return _toString;
            }

            var descriptors = new HashSet<string>
            {
                Limits.ToString(),
                GetRangesDescriptor(),
                GetSinglesDescriptor(),
                BuildTypeDescriptor(Variables[VariableClauseTypes.Is].ToList(), "Is"),
                GetPredicatesDescriptor()
            };

            descriptors.Remove(string.Empty);

            var descriptor = new StringBuilder();
            for (var idx = 0; idx < descriptors.Count; idx++)
            {
                descriptor.Append(descriptors.ElementAt(idx));
            }

            _toString = descriptor.ToString();
            IsDirty = false;
            return _toString;
        }

        public void SetExtents(T min, T max) => Limits.SetExtents(min, max);

        public virtual IParseTreeValue SelectExpressionValue
        {
            set
            {
                _selectExpressionValue = value;
                AddExpression(new IsClauseExpression(_selectExpressionValue, RelationalOperators.NEQ));
            }
            get => _selectExpressionValue;
        }

        public void AddExpression(IRangeClauseExpression expression)
        {
            if (expression is null || expression.ToString().Equals(string.Empty)) { return; }

            try
            {
                switch (expression)
                {
                    case IsClauseExpression isClause:
                        expression.IsUnreachable = !AddIsClause(isClause);
                        return;
                    case RangeOfValuesExpression rangeExpr:
                        expression.IsUnreachable = !AddRangeOfValuesExpression(rangeExpr);
                        return;
                    case ValueExpression valueExpr:
                        expression.IsUnreachable = !AddValueExpression(valueExpr);
                        return;
                    case UnaryExpression unaryExpr:
                        expression.IsUnreachable = !AddUnaryExpression(unaryExpr);
                        return;
                    case BinaryExpression binary:
                        expression.IsUnreachable = !AddBinaryExpression(binary);
                        return;
                    case LikeExpression like:
                        expression.IsUnreachable = !AddLikeExpression(like);
                        return;
                }
            }
            catch (ArgumentException)
            {
                expression.IsMismatch = true;
            }
        }

        public virtual bool HasFilters => Ranges.Any()
                    || SingleValues.Any()
                    || Limits.Any()
                    || this[VariableClauseTypes.Value].Any()
                    || this[VariableClauseTypes.Range].Any()
                    || this[VariableClauseTypes.Is].Any()
                    || this[VariableClauseTypes.Predicate].Any()
                    || LikePredicates.Any()
                    || ComparablePredicates.Any();

        public virtual bool FiltersAllValues
        {
            get
            {
                if (Limits.HasMinAndMaxLimits)
                {
                    return Limits.Minimum.CompareTo(Limits.Maximum) > 0
                        || Ranges.Any(rg => rg.Filters(Limits.Minimum, Limits.Maximum))
                        || SingleValues.Any(sv => Limits.Minimum.CompareTo(Limits.Maximum) == 0 && sv.CompareTo(Limits.Minimum) == 0);
                }
                return false;
            }
        }

        protected virtual bool TryGetMaximum(out T maximum) => Limits.TryGetMaximum(out maximum);

        protected virtual bool TryGetMinimum(out T minimum) => Limits.TryGetMinimum(out minimum);

        protected bool AddComparablePredicate(string lhs, IRangeClauseExpression expression)
        {
            if (FiltersTrueFalse) { return false; }

            var parseTreeValue = expression is IsClauseExpression ? expression.LHSValue : expression.RHSValue;
            if (!Converter(parseTreeValue.ValueText, out T expressionValue, _filterTypeName))
            {
                throw new ArgumentOutOfRangeException($"Unable to convert {parseTreeValue.ValueText} to {typeof(T)}");
            }

            if (ComparablePredicateFilters.ContainsKey(lhs))
            {
                var positiveLogic = ComparablePredicateFilters[lhs];
                if (!positiveLogic.FiltersAllValues)
                {
                    IRangeClauseExpression predicateExpression = new IsClauseExpression(parseTreeValue, expression.OpSymbol);
                    positiveLogic.AddExpression(predicateExpression);
                    if (positiveLogic.FiltersAllValues)
                    {
                        AddSingleValue(_trueValue);
                    }
                }

                var negativeLogic = ComparablePredicateFiltersInverse[lhs];
                if (!negativeLogic.FiltersAllValues)
                {
                    IRangeClauseExpression predicateExpressionInverse
                        = new IsClauseExpression(parseTreeValue, RelationalInverse(expression.OpSymbol));
                    negativeLogic.AddExpression(predicateExpressionInverse);
                    if (negativeLogic.FiltersAllValues)
                    {
                        AddSingleValue(_falseValue);
                    }
                }
            }

            var predicate = new PredicateValueExpression(lhs, expressionValue, expression.OpSymbol);
            var matchingVariablesNames = ComparablePredicates.Where(pv => pv.LHS.CompareTo(predicate.LHS) == 0);

            if (!matchingVariablesNames.Any(cv => cv.Equals(predicate)))
            {
                AddToContainer(ComparablePredicates, predicate);
                return true;
            }
            return false;
        }

        protected bool AddSingleValue(T value) => AddToContainer(SingleValues, value);

        protected virtual bool AddValueRange(RangeOfValues range)
        {
            if (FiltersRange(range))
            {
                return false;
            }

            IsDirty = true;

            range = Limits.HasMinimum ? range.TrimStart(Limits.Minimum) : range;
            range = Limits.HasMaximum ? range.TrimEnd(Limits.Maximum) : range;

            Ranges.RemoveWhere(rg => range.Filters(rg));

            var overlappingRanges = Ranges.Where(rg => range.Filters(rg.End) || range.Filters(rg.Start));
            if (overlappingRanges.Any())
            {
                (bool wasMerged, RangeOfValues mergedRange) = range.MergeWith(overlappingRanges.First());
                if (wasMerged)
                {
                    Ranges.Remove(overlappingRanges.First());
                    Ranges.Add(mergedRange);
                    return true;
                }
            }
            Ranges.Add(range);
            return true;
        }

        protected bool FiltersTrueFalse => FiltersValue(_trueValue) && FiltersValue(_falseValue);

        protected virtual bool AddIsClause(IsClauseExpression expression)
        {
            if (Converter(expression.LHS, out T value, _filterTypeName))
            {
                IsDirty = true;
                if (IsClauseAdders.ContainsKey(expression.OpSymbol))
                {
                    if (IsClauseAdders[expression.OpSymbol](this, value))
                    {
                        return true;
                    }
                }
                return false;
            }
            return AddToContainer(Variables[VariableClauseTypes.Is], expression.ToString());
        }

        protected virtual bool AddMinimum(T value)
        {
            IsDirty = true;
            var result = Limits.SetMinimum(value);
            if (TryGetMinimum(out T min))
            {
                var newRanges = new HashSet<RangeOfValues>();
                foreach ( var range in Ranges)
                {
                    newRanges.Add(range.TrimStart(min));
                }
                Ranges = newRanges;

                SingleValues.RemoveWhere(sv => sv.CompareTo(min) < 0);
            }
            return result;
        }

        protected virtual bool AddMaximum(T value)
        {
            IsDirty = true;
            var result =  Limits.SetMaximum(value);
            if (TryGetMaximum(out T max))
            {
                var newRanges = new HashSet<RangeOfValues>();
                foreach (var range in Ranges)
                {
                    newRanges.Add(range.TrimEnd(max));
                }
                Ranges = newRanges;

                SingleValues.RemoveWhere(sv => sv.CompareTo(max) > 0);
            }
            return result;
        }

        private (T start, T end) ConvertRangeValues(string startVal, string endVal)
        {
            if (!Converter(startVal, out T start, _filterTypeName) || !Converter(endVal, out T end, _filterTypeName))
            {
                throw new ArgumentException();
            }
            return (start, end);
        }

        protected bool AddToContainer<K>(HashSet<K> container, K value)
        {
            if (container.Contains(value))
            {
                return false;
            }
            IsDirty = true;
            container.Add(value);
            return true;
        }

        private bool FiltersRange(RangeOfValues rov)
        {
            return Limits.FiltersRange(rov.Start, rov.End)
                || Ranges.Any(rg => rg.Filters(rov));
        }

        private bool FiltersValue(T value) =>
            SingleValues.Contains(value)
            || Ranges.Any(rg => rg.Filters(value))
            || Limits.FiltersValue(value);

        private HashSet<string> this[VariableClauseTypes eType] => Variables[eType];

        private bool AddRangeOfValuesExpression(RangeOfValuesExpression rangeExpr)
        {
            if (rangeExpr.LHSValue.ParsesToConstantValue && rangeExpr.RHSValue.ParsesToConstantValue)
            {
                (T start, T end) = ConvertRangeValues(rangeExpr.LHS, rangeExpr.RHS);
                var rov = new RangeOfValues(start, end);
                if (!rov.IsMalformed)
                {
                    return rov.IsSingleValue ? AddSingleValue(rov.Start) : AddValueRange(rov);
                }
                return false;
            }
            return AddToContainer(Variables[VariableClauseTypes.Range], rangeExpr.ToString());
        }

        private bool AddValueExpression(ValueExpression valueExpr)
        {
            if (valueExpr.LHSValue.ParsesToConstantValue)
            {
                if (Converter(valueExpr.LHS, out T result, _filterTypeName))
                {
                    return FiltersValue(result) ? false : AddSingleValue(result);
                }
                throw new ArgumentException();
            }
            return AddToContainer(Variables[VariableClauseTypes.Value], valueExpr.ToString());
        }

        private bool AddUnaryExpression(UnaryExpression unaryExpr)
        {
            if (FiltersTrueFalse) { return false; }

            if (unaryExpr.LHSValue.ParsesToConstantValue)
            {
                if (Converter(unaryExpr.LHS, out T result, _filterTypeName))
                {
                    return FiltersValue(result) ? false : AddSingleValue(result);
                }
                throw new ArgumentException();
            }
            return AddToContainer(Variables[VariableClauseTypes.Predicate], unaryExpr.ToString());
        }

        private bool AddLikeExpression(LikeExpression like)
        {
            if (FiltersTrueFalse) { return false; }

            var addsSingleValue = false;
            if (like.Pattern.Equals("*"))
            {
                addsSingleValue = AddSingleValue(_trueValue);
            }
            if (LikePredicates.Any(pred => pred.Filters(like)))
            {
                return false;
            }
            return AddToContainer(LikePredicates, like);
        }

        private bool AddBinaryExpression(BinaryExpression binary)
        {
            if (FiltersTrueFalse && RelationalOperators.Includes(binary.OpSymbol))
            {
                return false;
            }

            if (!binary.LHSValue.ParsesToConstantValue && binary.RHSValue.ParsesToConstantValue)
            {
                if (!Converter(binary.RHS, out T value, _filterTypeName))
                {
                    throw new ArgumentException();
                }

                return AddComparablePredicate(binary.LHS, binary);
            }

            if (!binary.LHSValue.ParsesToConstantValue && !binary.RHSValue.ParsesToConstantValue)
            {
                return AddToContainer(Variables[VariableClauseTypes.Predicate], binary.ToString());
            }
            return false;
        }

        private static Dictionary<string, Func<ExpressionFilter<T>, T, bool>> IsClauseAdders = new Dictionary<string, Func<ExpressionFilter<T>, T, bool>>()
        {
            [RelationalOperators.LT] = delegate (ExpressionFilter<T> rg, T value) { return rg.AddMinimum(value); },
            [RelationalOperators.LTE] = delegate (ExpressionFilter<T> rg, T value) { var min = rg.AddMinimum(value); var val = rg.AddSingleValue(value); return min || val; },
            [RelationalOperators.LTE2] = delegate (ExpressionFilter<T> rg, T value) { var min = rg.AddMinimum(value); var val = rg.AddSingleValue(value); return min || val; },
            [RelationalOperators.GT] = delegate (ExpressionFilter<T> rg, T value) { return rg.AddMaximum(value); },
            [RelationalOperators.GTE] = delegate (ExpressionFilter<T> rg, T value) { var max = rg.AddMaximum(value); var val = rg.AddSingleValue(value); return max || val; },
            [RelationalOperators.GTE2] = delegate (ExpressionFilter<T> rg, T value) { var max = rg.AddMaximum(value); var val = rg.AddSingleValue(value); return max || val; },
            [RelationalOperators.EQ] = delegate (ExpressionFilter<T> rg, T value) { return rg.AddSingleValue(value); },
            [RelationalOperators.NEQ] = delegate (ExpressionFilter<T> rg, T value) { var min = rg.AddMinimum(value); var max = rg.AddMaximum(value); return min || max; },
        };

        private string RelationalInverse(string opSymbol)
            => RelationalInverses.Keys.Contains(opSymbol) ? RelationalInverses[opSymbol] : opSymbol;

        private static Dictionary<string, string> RelationalInverses = new Dictionary<string, string>()
        {
            [RelationalOperators.LT] = RelationalOperators.GTE,
            [RelationalOperators.LTE] = RelationalOperators.GTE,
            [RelationalOperators.LTE2] = RelationalOperators.GTE,
            [RelationalOperators.GT] = RelationalOperators.LTE,
            [RelationalOperators.GTE] = RelationalOperators.LT,
            [RelationalOperators.GTE2] = RelationalOperators.LT,
            [RelationalOperators.EQ] = RelationalOperators.NEQ,
            [RelationalOperators.NEQ] = RelationalOperators.EQ,
        };

        private string GetSinglesDescriptor()
        {
            var singles = SingleValues.Select(sv => sv.ToString()).ToList();
            singles.AddRange(this[VariableClauseTypes.Value]);
            return BuildTypeDescriptor(singles, "Values");
        }

        private string GetRangesDescriptor()
        {
            var values = Ranges.Select(rg => rg.ToString()).ToList();
            values.AddRange(this[VariableClauseTypes.Range]);
            return BuildTypeDescriptor(values, "Ranges");
        }

        private string GetPredicatesDescriptor()
        {
            var result = new HashSet<string>();
            foreach (var val in ComparablePredicates)
            {
                result.Add(val.ToString());
            }

            foreach (var like in LikePredicates)
            {
                result.Add(like.ToString());
            }

            foreach (var predicate in Variables[VariableClauseTypes.Predicate])
            {
                result.Add(predicate.ToString());
            }
            return BuildTypeDescriptor(result.ToList(), "Predicates");
        }

        private string BuildTypeDescriptor<K>(List<K> values, string identifier)
        {
            if (!values.Any()) { return string.Empty; }

            StringBuilder series = new StringBuilder();
            values.ForEach(val => series.Append($"{val},"));
            return $"{identifier}({series.ToString().Substring(0, series.Length - 1)})";
        }

        protected struct RangeOfValues
        {
            private readonly int _hashCode;
            private readonly string _toString;
            private readonly T _start;
            private readonly T _end;

            public RangeOfValues(T Start, T End)
            {
                _start = Start;
                _end = End;
                _toString = $"{_start}{":"}{_end}";
                _hashCode = _toString.GetHashCode();
            }

            public override string ToString() => _toString;

            public override int GetHashCode() => _hashCode;

            public override bool Equals(object obj)
            {
                if (!(obj is RangeOfValues rov))
                {
                    return false;
                }
                return _toString.Equals(rov.ToString());
            }

            public bool IsMalformed => typeof(T) != typeof(bool) ? _start.CompareTo(_end) > 0
                                                                        : _end.CompareTo(_start) > 0;

            public bool IsSingleValue => _start.CompareTo(_end) == 0;

            public T Start => _start;

            public T End => _end;

            public RangeOfValues TrimStart(T value)
                => Start.CompareTo(value) < 0 ? new RangeOfValues(value, End) : new RangeOfValues(Start, End);

            public RangeOfValues TrimEnd(T value)
                => End.CompareTo(value) > 0 ? new RangeOfValues(Start, value) : new RangeOfValues(Start, End);

            public (bool wasMerged, RangeOfValues mergedRov) MergeWith(RangeOfValues rov)
            {
                if (Filters(rov.Start) || Filters(rov.End))
                {
                    var newStart = Start.CompareTo(rov.Start) < 0 ? Start : rov.Start;
                    var newEnd = End.CompareTo(rov.End) > 0 ? End : rov.End;
                    return (true, new RangeOfValues(newStart, newEnd));
                }
                return (false, new RangeOfValues(Start, End));
            }

            public bool Filters(T value)
                => Start.CompareTo(value) <= 0 && End.CompareTo(value) >= 0;

            public bool Filters(RangeOfValues rov)
                => Filters(rov.Start, rov.End);

            public bool Filters(T start, T end)
                => Start.CompareTo(start) <= 0 && End.CompareTo(end) >= 0;
        }
    }
}
