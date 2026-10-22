using Microsoft.AspNetCore.Mvc;

namespace Modules.Shared.Common;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiBaseController : ControllerBase
{
}
