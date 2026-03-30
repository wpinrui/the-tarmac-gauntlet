import type { CarClass } from "../types";

export function ClassBadge({ carClass }: { carClass: CarClass }) {
  return (
    <span className={`class-badge ${carClass.toLowerCase()}`}>
      Class {carClass}
    </span>
  );
}
