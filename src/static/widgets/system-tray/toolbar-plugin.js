const GUIDS_TO_IGNORE = [
  "7820ae73-23e3-4229-82c1-e41cb67d5b9c", // speaker volument icon
  "7820ae74-23e3-4229-82c1-e41cb67d5b9c", // network icon
  "7820ae75-23e3-4229-82c1-e41cb67d5b9c", // battery icon
];

const items = trayIcons
  .filter((icon) => icon.isVisible && (self.desktopSuite || icon.isPromoted) && !GUIDS_TO_IGNORE.includes(icon.guid))
  .map((icon) => {
    const onClick = `invoke(SeelenCommand.SendSystemTrayIconAction, {
        id: "${icon.registryKey}",
        action: "LeftClick",
      })`;

    const onAuxClick = `invoke(SeelenCommand.SendSystemTrayIconAction, {
        id: "${icon.registryKey}",
        action: "MiddleClick",
      })`;

    const onContextMenu = `invoke(SeelenCommand.SendSystemTrayIconAction, {
        id: "${icon.registryKey}",
        action: "RightClick",
      })`;

    return Button({
      tooltip: icon.tooltip,
      onClick,
      onAuxClick,
      onContextMenu,
      content: Image({
        path: icon.iconPath,
      }),
    });
  });

// The suite shows every registered visible app icon in place. Keep upstream's
// manual pin/overflow behavior for non-suite profiles without changing Windows
// promotion settings or maintaining another list of running applications.
if (!self.desktopSuite) {
  items.push(Button({
    tooltip: "System Tray",
    content: Icon({ name: "IoIosArrowDropdown" }),
    onClick: `trigger("@seelen/system-tray");`,
  }));
}

if (self.placement === "left") {
  items.reverse();
}

return items;
