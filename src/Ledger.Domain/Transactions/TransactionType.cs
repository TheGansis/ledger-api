namespace Ledger.Domain.Transactions;

public enum TransactionType
{
    Deposit = 0,     // внешний приход на счёт
    Withdrawal = 1,  // внешний расход со счёта
    Transfer = 2,    // перевод между счетами внутри системы
}

public enum TransactionStatus
{
    Completed = 0,
    Rejected = 1,
}
