"""Independent decimal check of a marker package's deterministic unit geometry."""

import json
import sys
from decimal import Decimal


def number(value):
    return Decimal(str(value))


def check(data):
    count = int(data["MarkerCount"])
    length = number(data["MarkerLength"])
    diameter = number(data["MarkerDiameter"])
    plan = data["Assembly"]
    total = number(plan["TotalClearance"])
    gap = number(plan["BetweenGap"])
    divider = number(plan["DividerThickness"])
    wall = number(plan["WallThickness"])
    end = number(plan["LengthClearance"])
    combined = bool(plan.get("CombinedLengthClearance", False))
    axis = plan["LengthAxis"]
    orientation = plan["Orientation"]
    specified_tray_height = data.get("TrayHeightMm")
    if specified_tray_height is not None:
        specified_tray_height = number(specified_tray_height)
    units = plan["Units"]

    if count <= 0 or length <= 0 or diameter <= 0 or wall <= 0 or total < 0 or gap < 0 or divider < 0 or end < 0 or (combined and not 4 <= end <= 6):
        raise ValueError("Invalid product dimensions or allowances")
    if not units or sum(int(unit["Count"]) for unit in units) != count:
        raise ValueError("Unit counts do not equal the product count")
    if axis not in ("Width", "Depth", "Height"):
        raise ValueError("Invalid pen length axis")
    if (orientation == "vertical") != (axis == "Height"):
        raise ValueError("Pen orientation and length axis disagree")

    checked = []
    for unit in units:
        pens = int(unit["Count"])
        if pens <= 0 or int(unit["Rows"]) != 1 or int(unit["Columns"]) != pens:
            raise ValueError("Every display unit must expose a complete single row")
        row = pens * diameter + total + (pens - 1) * (gap + divider) + 2 * wall
        thin = diameter + total + 2 * wall
        long = length + end + 2 * wall
        minimum_height = long if axis == "Height" else thin
        if specified_tray_height is not None and specified_tray_height < minimum_height:
            raise ValueError(f"Unit {unit['Id']} specified tray height is below the minimum")
        expected = {
            "Width": long if axis == "Width" else row,
            "Depth": long if axis == "Depth" else row if axis == "Width" else thin,
            "Height": specified_tray_height if specified_tray_height is not None else minimum_height,
        }
        for name, value in expected.items():
            if number(unit[name]) != value:
                raise ValueError(f"Unit {unit['Id']} {name} differs from calculated geometry")
        checked.append({"Id": unit["Id"], "Count": pens, "Width": str(expected["Width"]), "Depth": str(expected["Depth"]), "Height": str(expected["Height"])})

    def overlap(first, second, state, coordinate, size):
        return (number(first[state][coordinate]) < number(second[state][coordinate]) + number(second[size])
                and number(second[state][coordinate]) < number(first[state][coordinate]) + number(first[size]))

    for index, current in enumerate(units):
        for earlier in units[:index]:
            if all(overlap(current, earlier, "Closed", coordinate, size)
                   for coordinate, size in (("X", "Width"), ("Y", "Depth"), ("Z", "Height"))):
                raise ValueError("Closed display units overlap")
            visible_axes = (("X", "Width"), ("Z", "Height")) if orientation == "vertical" else (("X", "Width"), ("Y", "Depth"))
            if all(overlap(current, earlier, "Open", coordinate, size) for coordinate, size in visible_axes):
                raise ValueError("Opened display units obscure one another")

    package = [number(data["PackageDimensions"][name]) for name in ("Longest", "Middle", "Shortest")]
    limits = [number(data["ClosedLimits"][name]) for name in ("Longest", "Middle", "Shortest")]
    if package != sorted(package, reverse=True) or any(edge <= 0 or edge > limit for edge, limit in zip(package, limits)):
        raise ValueError("Closed package exceeds its sorted shipping limits")
    return {"Ok": True, "PenCount": count, "Units": checked}


if __name__ == "__main__":
    try:
        print(json.dumps(check(json.load(sys.stdin)), ensure_ascii=False))
    except (ValueError, KeyError, TypeError, ArithmeticError) as error:
        print(json.dumps({"Ok": False, "Error": str(error)}, ensure_ascii=False))
        sys.exit(2)
