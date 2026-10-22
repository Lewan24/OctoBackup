namespace Modules.Auth.Shared.Static.Entities;

public static class AppRoles
{
    public const string User = nameof(User);
    public const string Admin = nameof(Admin);

    private static readonly List<string> MainRolesList = new()
    {
        User,
        Admin
    };

    public static List<string> GetAll()
    {
        var list = new List<string>();

        list.AddRange(MainRolesList);

        return list;
    }
}
