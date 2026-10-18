export interface Location {
  latitude: number;
  longitude: number;
}
export const demoLocations = [
  { name: "Dhaka", latitude: 23.8103, longitude: 90.4125 },
  { name: "Daudkandi", latitude: 23.534, longitude: 90.7112 },
  { name: "Cumilla", latitude: 23.4607, longitude: 91.1809 },
  { name: "Feni", latitude: 23.0159, longitude: 91.3976 },
  { name: "Chattogram", latitude: 22.3569, longitude: 91.7832 },
];
export function distanceKm(a: Location, b: Location) {
  const rad = (d: number) => (d * Math.PI) / 180;
  const x =
    Math.sin(rad(b.latitude - a.latitude) / 2) ** 2 +
    Math.cos(rad(a.latitude)) *
      Math.cos(rad(b.latitude)) *
      Math.sin(rad(b.longitude - a.longitude) / 2) ** 2;
  return (
    6371 *
    2 *
    Math.atan2(Math.sqrt(Math.min(1, x)), Math.sqrt(Math.max(0, 1 - x)))
  );
}
