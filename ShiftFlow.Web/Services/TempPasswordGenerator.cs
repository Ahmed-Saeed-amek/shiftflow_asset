using System.Security.Cryptography;

namespace ShiftFlow.Web.Services;

/// <summary>Generates a temporary password guaranteed to satisfy Identity's password policy
/// (Program.cs requires upper/lower/digit/non-alphanumeric) — extracted from UsersController,
/// which had this right, after VendorsController.CreateLogin/ResetPassword were found using an ad-hoc
/// GUID-substring scheme that never produces a special character, so every vendor login
/// creation/reset failed Identity's validator 100% of the time (confirmed live).</summary>
public static class TempPasswordGenerator
{
    public static string Generate()
    {
        // Produces e.g. "Sf@mK7xP2nQa" — satisfies uppercase, lowercase, digit, special
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string special = "@#!$";

        Span<char> pwd = stackalloc char[12];
        pwd[0] = Pick(upper);
        pwd[1] = Pick(lower);
        pwd[2] = Pick(digits);
        pwd[3] = Pick(special);
        const string all = lower + upper + digits + special;
        for (int i = 4; i < 12; i++) pwd[i] = Pick(all);

        // Shuffle
        for (int i = 11; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (pwd[i], pwd[j]) = (pwd[j], pwd[i]);
        }

        return new string(pwd);

        static char Pick(string chars) =>
            chars[RandomNumberGenerator.GetInt32(chars.Length)];
    }
}
