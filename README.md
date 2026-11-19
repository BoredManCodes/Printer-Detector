# Printer Detector

A Windows tool for field techs. Copy one exe onto a PC, run it, and it lists every printer configured on that machine: name, driver, status, connection type, IP or port, warning flags & toner levels. Nothing gets installed.

Built by Border Tech Solutions.

## What it reads

The printer list comes from WMI. `Win32_Printer` supplies name, driver & status, and network ports are matched against `Win32_TCPIPPrinterPort` to get the IP. Ports named `USB###` show as USB, `LPT#` and `COM#` as local, and everything else as network.

Toner levels come over SNMP v1 with the `public` community string, read from the Printer MIB (RFC 3805, OID branch `1.3.6.1.2.1.43.11`). Each printer is queried on its own task with a 1 second timeout, so a dead printer shows "Unavailable" without freezing the list. USB & local printers have no IP, so they're skipped.

## Actions

Select a printer and the details panel on the right offers:

- Open the web GUI over HTTP or HTTPS, copy the address, search the driver name on Google
- Print a test page, pause, resume, cancel all jobs, set as default, rename, delete
- Set comment & location, toggle sharing
- Ping, test port 9100, resolve the MAC address, send Wake-on-LAN, open traceroute or continuous ping
- Launch a VNC viewer against the printer (downloaded on first use)
- Restart the Print Spooler, clear a stuck print queue

The header has three more entry points:

- **Scan Network** checks the local subnets for devices answering on ports 9100, 631 or 515 that aren't added to this PC yet. Scans are capped at a /22 so a VPN adapter with a 255.0.0.0 mask can't hang it.
- **System tools** opens the Add Printer wizard, Devices and Printers, Print Management, Device Manager, Services, Event Viewer or the spool folder, and exports the printer list to CSV.
- **Utilities** installs the EKM Insight DCA toner-monitoring agent silently from a pasted license key, and shows its license status.

Spooler, queue & DCA actions need administrator rights.

## Bundles

The **Bundles** menu in the header copies print queues between PCs. Each printer's details panel also has a **Save to bundle** button.

- **Save** writes the ticked queues to `Bundles\<name>` next to the exe: the driver package from the driver store, the port, and the queue's paper size, tray and media weight. Those settings live in the queue's DEVMODE in the registry, not in the print ticket, so they are saved separately. A per-user copy (Printing Preferences) wins over the machine copy (Printing Defaults).
- **Install** takes a bundle from the picker and adds the ticked queues. It installs the driver with `pnputil`, creates the port, adds the queue, then stops the spooler, writes the saved settings into the registry and starts the spooler again. Queues that already exist are skipped unless **Replace** is ticked. Needs administrator rights.
- **Remove several printers** deletes the ticked queues. Drivers and ports stay installed.
- **Inspect selected** shows the decoded paper size, input tray, quality, duplex and media type, the installed options and the print ticket. For a bundle it also lists what an install would change on this PC and compares against a queue of the same name that is already there. Driver-private bytes, where the tray and weight choices of some drivers live, are counted but not decoded.

Port handling on install: a Standard TCP/IP or LPR port is recreated as it was. A vendor port such as EpsonNet Print Port can't be copied, so the printer's address is read from the port and a Standard TCP/IP port named `IP_<address>` (RAW 9100) is created instead. USB and WSD ports can't be created from a bundle, so the queue is added on the `NUL:` placeholder port and the log lists the steps to pick the real port under Printer properties > Ports.

The work is done by the PowerShell scripts in `PrinterDetector/Assets/Scripts`, which are embedded in the exe.

## Building

Needs the .NET 8 SDK on Windows.

```
cd PrinterDetector
dotnet build
```

To produce the single-file exe:

```
dotnet publish -c Release
```

The csproj already sets `win-x64`, self-contained & single-file, so the output in `bin/Release/net8.0-windows/win-x64/publish/` runs on a PC with no .NET runtime installed. WPF-only native files that the Windows Desktop runtime pack bundles get stripped at publish, since WinForms never loads them.

## Dependencies

- [Lextm.SharpSnmpLib](https://github.com/lextm/sharpsnmplib) 12.5.5 for SNMP
- System.Management 8.0.0 for WMI
- System.ServiceProcess.ServiceController 8.0.0 for the spooler

## Testing

No automated tests. It talks to WMI & real printers over SNMP, so testing is manual: run it on a PC with at least one SNMP-enabled network printer and one USB printer, check every column fills in, then add a printer at an unreachable IP and confirm the list still loads.
