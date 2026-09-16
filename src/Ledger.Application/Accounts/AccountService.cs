using FluentValidation;
using Ledger.Application.Abstractions;
using Ledger.Application.Common;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Accounts;

public sealed class OpenAccountValidator : AbstractValidator<OpenAccountRequest>
{
    public OpenAccountValidator()
    {
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.OwnerId).MaximumLength(128);
    }
}

public sealed class AccountService(IAccountRepository accounts, ITransactionRepository transactions, IUnitOfWork uow, IClock clock, IAccountCache cache, ICurrentUser user)
{
    public async Task<AccountDto> OpenAsync(OpenAccountRequest request, CancellationToken ct)
    {
        if (request.OwnerId is not null && !user.IsAdmin)
            throw new ForbiddenException("Only administrators can open accounts for other owners.");
        var account = Account.Open(request.OwnerId ?? user.Subject, request.OwnerName, request.Currency, clock.UtcNow);
        accounts.Add(account);
        await uow.SaveChangesAsync(ct);
        return AccountDto.From(account);
    }

    public async Task<AccountDto?> GetAsync(Guid id, CancellationToken ct)
    {
        if (await cache.GetAsync(id, ct) is { } cached) return Authorize(cached);
        var account = await accounts.FindAsync(id, ct);
        if (account is null) return null;
        var dto = AccountDto.From(account);
        await cache.SetAsync(dto, ct);
        return Authorize(dto);
    }

    public async Task<Page<LedgerEntryDto>?> GetStatementAsync(Guid id, string? cursor, int limit, CancellationToken ct)
    {
        var account = await accounts.FindAsync(id, ct);
        if (account is null) return null;
        EnsureCanAccess(account.OwnerId);
        var page = await transactions.GetEntriesAsync(id, cursor, Math.Clamp(limit, 1, 200), ct);
        var items = page.Items.Select(e => new LedgerEntryDto(e.Id, e.TransactionId, e.Amount, e.BalanceAfter, e.CreatedAt)).ToList();
        return new Page<LedgerEntryDto>(items, page.NextCursor);
    }

    /// <summary>Заморозка/закрытие — операции комплаенса, только для администратора.</summary>
    public async Task<AccountDto?> SetStatusAsync(Guid id, AccountStatus status, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new ForbiddenException("Only administrators can change account status.");
        var dto = await uow.ExecuteInTransactionAsync(async token =>
        {
            var locked = await accounts.GetForUpdateAsync([id], token);
            if (!locked.TryGetValue(id, out var account)) return null;
            switch (status)
            {
                case AccountStatus.Frozen: account.Freeze(); break;
                case AccountStatus.Active: account.Unfreeze(); break;
                case AccountStatus.Closed: account.Close(); break;
            }
            await uow.SaveChangesAsync(token);
            return AccountDto.From(account);
        }, ct);
        if (dto is not null) await cache.InvalidateAsync([id], ct);
        return dto;
    }

    private AccountDto Authorize(AccountDto dto) { EnsureCanAccess(dto.OwnerId); return dto; }

    /// <summary>403, а не 404: чужой счёт существует, но недоступен. Альтернатива — 404, чтобы скрыть сам факт существования.</summary>
    private void EnsureCanAccess(string ownerId)
    {
        if (!user.IsAdmin && !string.Equals(ownerId, user.Subject, StringComparison.Ordinal))
            throw new ForbiddenException("Account belongs to another owner.");
    }
}
