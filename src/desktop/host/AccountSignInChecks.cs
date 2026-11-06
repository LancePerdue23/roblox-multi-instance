namespace RobloxMultiInstance.Desktop;

// One automatic identity request at a time; navigation cannot bypass backoff.
internal sealed class AccountSignInChecks
{
    bool checking;
    long next;
    internal bool Begin(long now)
    {
        if(checking||now<next)return false;
        checking=true;return true;
    }
    internal void Complete(string status,long now)
    {
        checking=false;
        next=now+(status switch {"RateLimited"=>60000,"NoSession"=>2000,_=>10000});
    }
}
