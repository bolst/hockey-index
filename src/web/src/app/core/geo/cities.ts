export interface City {
  name: string;
  region: string;
  lat: number;
  lng: number;
}

/**
 * Manual location choices when the browser and IP lookups both fail. There is no public geocoder
 * (address search is host-only and metered), so this static list covers the main hockey markets.
 */
export const CITIES: readonly City[] = [
  { name: 'Toronto', region: 'ON', lat: 43.65, lng: -79.38 },
  { name: 'Mississauga', region: 'ON', lat: 43.59, lng: -79.64 },
  { name: 'Brampton', region: 'ON', lat: 43.73, lng: -79.76 },
  { name: 'Markham', region: 'ON', lat: 43.86, lng: -79.34 },
  { name: 'Oshawa', region: 'ON', lat: 43.9, lng: -78.86 },
  { name: 'Hamilton', region: 'ON', lat: 43.26, lng: -79.87 },
  { name: 'Burlington', region: 'ON', lat: 43.33, lng: -79.8 },
  { name: 'Barrie', region: 'ON', lat: 44.39, lng: -79.69 },
  { name: 'Kitchener', region: 'ON', lat: 43.45, lng: -80.49 },
  { name: 'Guelph', region: 'ON', lat: 43.54, lng: -80.25 },
  { name: 'London', region: 'ON', lat: 42.98, lng: -81.25 },
  { name: 'Windsor', region: 'ON', lat: 42.31, lng: -83.04 },
  { name: 'Kingston', region: 'ON', lat: 44.23, lng: -76.49 },
  { name: 'Ottawa', region: 'ON', lat: 45.42, lng: -75.7 },
  { name: 'Sudbury', region: 'ON', lat: 46.49, lng: -80.99 },
  { name: 'Thunder Bay', region: 'ON', lat: 48.38, lng: -89.25 },
  { name: 'Montréal', region: 'QC', lat: 45.5, lng: -73.57 },
  { name: 'Québec City', region: 'QC', lat: 46.81, lng: -71.21 },
  { name: 'Gatineau', region: 'QC', lat: 45.48, lng: -75.7 },
  { name: 'Sherbrooke', region: 'QC', lat: 45.4, lng: -71.89 },
  { name: 'Halifax', region: 'NS', lat: 44.65, lng: -63.58 },
  { name: 'Moncton', region: 'NB', lat: 46.09, lng: -64.78 },
  { name: 'Fredericton', region: 'NB', lat: 45.96, lng: -66.64 },
  { name: 'Saint John', region: 'NB', lat: 45.27, lng: -66.06 },
  { name: 'Charlottetown', region: 'PE', lat: 46.24, lng: -63.13 },
  { name: "St. John's", region: 'NL', lat: 47.56, lng: -52.71 },
  { name: 'Winnipeg', region: 'MB', lat: 49.9, lng: -97.14 },
  { name: 'Regina', region: 'SK', lat: 50.45, lng: -104.61 },
  { name: 'Saskatoon', region: 'SK', lat: 52.13, lng: -106.67 },
  { name: 'Calgary', region: 'AB', lat: 51.05, lng: -114.07 },
  { name: 'Edmonton', region: 'AB', lat: 53.55, lng: -113.49 },
  { name: 'Red Deer', region: 'AB', lat: 52.27, lng: -113.81 },
  { name: 'Vancouver', region: 'BC', lat: 49.28, lng: -123.12 },
  { name: 'Surrey', region: 'BC', lat: 49.19, lng: -122.85 },
  { name: 'Burnaby', region: 'BC', lat: 49.25, lng: -122.98 },
  { name: 'Victoria', region: 'BC', lat: 48.43, lng: -123.37 },
  { name: 'Kelowna', region: 'BC', lat: 49.89, lng: -119.5 },
  { name: 'Kamloops', region: 'BC', lat: 50.67, lng: -120.33 },
  { name: 'Whitehorse', region: 'YT', lat: 60.72, lng: -135.06 },
  { name: 'Yellowknife', region: 'NT', lat: 62.45, lng: -114.37 },
  { name: 'Boston', region: 'MA', lat: 42.36, lng: -71.06 },
  { name: 'Worcester', region: 'MA', lat: 42.26, lng: -71.8 },
  { name: 'Providence', region: 'RI', lat: 41.82, lng: -71.41 },
  { name: 'Hartford', region: 'CT', lat: 41.76, lng: -72.69 },
  { name: 'Portland', region: 'ME', lat: 43.66, lng: -70.26 },
  { name: 'Manchester', region: 'NH', lat: 42.99, lng: -71.46 },
  { name: 'Burlington', region: 'VT', lat: 44.48, lng: -73.21 },
  { name: 'New York', region: 'NY', lat: 40.71, lng: -74.01 },
  { name: 'Buffalo', region: 'NY', lat: 42.89, lng: -78.88 },
  { name: 'Rochester', region: 'NY', lat: 43.16, lng: -77.61 },
  { name: 'Syracuse', region: 'NY', lat: 43.05, lng: -76.15 },
  { name: 'Albany', region: 'NY', lat: 42.65, lng: -73.75 },
  { name: 'Newark', region: 'NJ', lat: 40.74, lng: -74.17 },
  { name: 'Philadelphia', region: 'PA', lat: 39.95, lng: -75.17 },
  { name: 'Pittsburgh', region: 'PA', lat: 40.44, lng: -80 },
  { name: 'Washington', region: 'DC', lat: 38.91, lng: -77.04 },
  { name: 'Baltimore', region: 'MD', lat: 39.29, lng: -76.61 },
  { name: 'Raleigh', region: 'NC', lat: 35.78, lng: -78.64 },
  { name: 'Charlotte', region: 'NC', lat: 35.23, lng: -80.84 },
  { name: 'Nashville', region: 'TN', lat: 36.16, lng: -86.78 },
  { name: 'Atlanta', region: 'GA', lat: 33.75, lng: -84.39 },
  { name: 'Tampa', region: 'FL', lat: 27.95, lng: -82.46 },
  { name: 'Miami', region: 'FL', lat: 25.76, lng: -80.19 },
  { name: 'Orlando', region: 'FL', lat: 28.54, lng: -81.38 },
  { name: 'Detroit', region: 'MI', lat: 42.33, lng: -83.05 },
  { name: 'Grand Rapids', region: 'MI', lat: 42.96, lng: -85.67 },
  { name: 'Columbus', region: 'OH', lat: 39.96, lng: -83 },
  { name: 'Cleveland', region: 'OH', lat: 41.5, lng: -81.69 },
  { name: 'Cincinnati', region: 'OH', lat: 39.1, lng: -84.51 },
  { name: 'Indianapolis', region: 'IN', lat: 39.77, lng: -86.16 },
  { name: 'Chicago', region: 'IL', lat: 41.88, lng: -87.63 },
  { name: 'Milwaukee', region: 'WI', lat: 43.04, lng: -87.91 },
  { name: 'Madison', region: 'WI', lat: 43.07, lng: -89.4 },
  { name: 'Minneapolis', region: 'MN', lat: 44.98, lng: -93.27 },
  { name: 'Saint Paul', region: 'MN', lat: 44.95, lng: -93.09 },
  { name: 'Duluth', region: 'MN', lat: 46.79, lng: -92.1 },
  { name: 'Fargo', region: 'ND', lat: 46.88, lng: -96.79 },
  { name: 'St. Louis', region: 'MO', lat: 38.63, lng: -90.2 },
  { name: 'Kansas City', region: 'MO', lat: 39.1, lng: -94.58 },
  { name: 'Omaha', region: 'NE', lat: 41.26, lng: -95.93 },
  { name: 'Dallas', region: 'TX', lat: 32.78, lng: -96.8 },
  { name: 'Houston', region: 'TX', lat: 29.76, lng: -95.37 },
  { name: 'Austin', region: 'TX', lat: 30.27, lng: -97.74 },
  { name: 'Denver', region: 'CO', lat: 39.74, lng: -104.99 },
  { name: 'Colorado Springs', region: 'CO', lat: 38.83, lng: -104.82 },
  { name: 'Salt Lake City', region: 'UT', lat: 40.76, lng: -111.89 },
  { name: 'Phoenix', region: 'AZ', lat: 33.45, lng: -112.07 },
  { name: 'Las Vegas', region: 'NV', lat: 36.17, lng: -115.14 },
  { name: 'Los Angeles', region: 'CA', lat: 34.05, lng: -118.24 },
  { name: 'Anaheim', region: 'CA', lat: 33.84, lng: -117.91 },
  { name: 'San Diego', region: 'CA', lat: 32.72, lng: -117.16 },
  { name: 'San Jose', region: 'CA', lat: 37.34, lng: -121.89 },
  { name: 'San Francisco', region: 'CA', lat: 37.77, lng: -122.42 },
  { name: 'Sacramento', region: 'CA', lat: 38.58, lng: -121.49 },
  { name: 'Portland', region: 'OR', lat: 45.52, lng: -122.68 },
  { name: 'Seattle', region: 'WA', lat: 47.61, lng: -122.33 },
  { name: 'Spokane', region: 'WA', lat: 47.66, lng: -117.43 },
  { name: 'Anchorage', region: 'AK', lat: 61.22, lng: -149.9 },
];

export function cityLabel(city: City): string {
  return `${city.name}, ${city.region}`;
}

export function searchCities(query: string, limit = 8): City[] {
  const needle = normalize(query);
  if (!needle) {
    return CITIES.slice(0, limit);
  }
  const starts = CITIES.filter((city) => normalize(cityLabel(city)).startsWith(needle));
  const contains = CITIES.filter((city) => !starts.includes(city) && normalize(cityLabel(city)).includes(needle));
  return [...starts, ...contains].slice(0, limit);
}

const NEAREST_CITY_MAX_KM = 40;

/** Nearest listed city within 40 km, used to label a coordinate ("Near Toronto, ON"). */
export function nearestCity(lat: number, lng: number): City | null {
  let best: City | null = null;
  let bestKm = NEAREST_CITY_MAX_KM;
  for (const city of CITIES) {
    const km = distanceKm(lat, lng, city.lat, city.lng);
    if (km <= bestKm) {
      best = city;
      bestKm = km;
    }
  }
  return best;
}

function distanceKm(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const toRad = Math.PI / 180;
  const dLat = (lat2 - lat1) * toRad;
  const dLng = (lng2 - lng1) * toRad;
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(lat1 * toRad) * Math.cos(lat2 * toRad) * Math.sin(dLng / 2) ** 2;
  return 12742 * Math.asin(Math.sqrt(a));
}

function normalize(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim();
}
