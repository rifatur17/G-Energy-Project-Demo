using System.Net;
using GEnergy.Api.Entities;
using MimeKit;
namespace GEnergy.Api.Services.Email;

public static class WelcomeEmailMessage
{
    public static MimeMessage Create(WelcomeEmail delivery, EmailOptions options)
    {
        var user = delivery.User;
        var role = user.Role == "CarOwner" ? "Car Owner" : "Car Driver";
        var message = new MimeMessage();
        var sender = MailboxAddress.Parse(options.FromAddress);
        message.From.Add(new MailboxAddress(options.FromName, sender.Address));
        message.To.Add(new MailboxAddress("", user.Email));
        message.Subject = "Welcome to G Energy - keep your User ID";
        message.MessageId = $"g-energy-welcome-{delivery.Id}@{sender.Address.Split('@')[1]}";
        static string H(string value) => WebUtility.HtmlEncode(value);
        message.Body = new BodyBuilder
        {
            TextBody = $"Welcome to G Energy, {user.FullName}!\n\nThank you for creating your account.\n\nYour User ID: {user.UserCode}\nAccount type: {role}\nLogin: {options.LoginUrl}\n\nKeep this email so you can find your User ID later. Log in with this User ID and the password you chose at signup. For your security, we never include your password in email.\n\nAll charging and payments in this project are simulated.\n\nThank you for joining us!\nThe G Energy team",
            HtmlBody = $"<h1>Welcome to G Energy, {H(user.FullName)}!</h1><p>Thank you for creating your account.</p><p>Your User ID: <strong>{H(user.UserCode)}</strong><br>Account type: {role}</p><p><a href=\"{H(options.LoginUrl)}\">Log in to G Energy</a></p><p>Keep this email so you can find your User ID later. Use this ID and the password you chose at signup. For your security, we never include your password in email.</p><p>All charging and payments in this project are simulated.</p><p>Thank you for joining us!<br>The G Energy team</p>"
        }.ToMessageBody();
        return message;
    }
}
