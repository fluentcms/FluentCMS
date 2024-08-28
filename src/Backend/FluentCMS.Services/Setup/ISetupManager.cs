﻿using FluentCMS.Services.Setup.Models;

namespace FluentCMS.Services.Setup;

public interface ISetupManager
{
    Task<bool> IsInitialized();
    Task Reset();
    Task<bool> Start(SetupModel request);
}
