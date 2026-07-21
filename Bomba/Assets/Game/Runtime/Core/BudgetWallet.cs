using System;

namespace BriefcaseProtocol.Core
{
    [Serializable]
    public sealed class BudgetWallet
    {
        public int Initial { get; private set; }
        public int Remaining { get; private set; }
        public int Spent => Initial - Remaining;

        public BudgetWallet(int initial)
        {
            Reset(initial);
        }

        public bool CanAfford(int cost) => cost >= 0 && Remaining >= cost;

        public bool TrySpend(int cost)
        {
            if (!CanAfford(cost))
            {
                return false;
            }

            Remaining -= cost;
            return true;
        }

        public bool TryRefund(int cost)
        {
            if (cost < 0 || Remaining + cost > Initial)
            {
                return false;
            }

            Remaining += cost;
            return true;
        }

        public void Reset(int initial)
        {
            if (initial < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initial));
            }

            Initial = initial;
            Remaining = initial;
        }
    }
}
