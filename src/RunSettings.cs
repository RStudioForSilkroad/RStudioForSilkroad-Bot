using System;

sealed partial class MemorySelectV3Form
{
    bool RunSendKeys
    {
        get { return sendKeys.Checked; }
    }

    bool RunZerk
    {
        get { return autoZerk.Checked; }
    }

    bool RunSpeed
    {
        get { return autoSpeed.Checked; }
    }

    bool RunRepair
    {
        get { return autoRepair.Checked; }
    }

    bool RunRecovery
    {
        get { return autoRecovery.Checked; }
    }

    int RunDelay
    {
        get { return (int)delay.Value; }
    }

    int RunTimeout
    {
        get { return (int)timeout.Value; }
    }

    int RunWait
    {
        get { return (int)reviveSeconds.Value; }
    }

    int RunDeathLimit
    {
        get { return (int)deathLimit.Value; }
    }

    string RunKeys
    {
        get { return keys.Text; }
    }

    string RunBerserkClass
    {
        get { return berserkClass.SelectedItem as string; }
    }

    void StartRequested()
    {
        if (active)
            return;
        if (tabs.SelectedIndex == 2)
            StartGrimRequested();
        else
            Start();
    }
}
