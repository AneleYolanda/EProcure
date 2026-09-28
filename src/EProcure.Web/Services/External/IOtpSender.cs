namespace EProcure.Web.Services.External
{
    public interface IOtpSender
    {
        System.Threading.Tasks.Task SendOtpAsync(string phoneNumber, string code);
    }
}
