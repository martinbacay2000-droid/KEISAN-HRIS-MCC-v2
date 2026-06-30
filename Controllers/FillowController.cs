using KEISAN_HRIS_v2.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Fillow.Controllers
{
    public class FillowController : BaseController
    {
        // ------------------ Dashboards ------------------
        public IActionResult DashboardLight() => View();
        public IActionResult DashboardDark() => View();

        // ------------------ Pages ------------------
        public IActionResult PageRegister() => View();
        public IActionResult Login() => View();
        public IActionResult PageLockScreen() => View();
        public IActionResult PageError400() => View();
        public IActionResult PageError403() => View();
        public IActionResult PageError404() => View();
        public IActionResult PageError500() => View();
        public IActionResult PageError503() => View();
    }
}
