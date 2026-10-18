import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import {
  connectors,
  useMobilityApi,
  type Station,
} from "../features/mobility/api";
import {
  demoLocations,
  distanceKm,
  type Location,
} from "../features/discovery/geo";
import { StationMap } from "../features/discovery/StationMap";
export function Stations() {
  const api = useMobilityApi();
  const [params] = useSearchParams();
  const requested = params.get("connector") ?? "";
  const [connector, setConnector] = useState(
    connectors.some((c) => c === requested) ? requested : "",
  );
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [stations, setStations] = useState<Station[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [origin, setOrigin] = useState<Location>(demoLocations[0]);
  const [locationName, setLocationName] = useState("Dhaka");
  const [locationMessage, setLocationMessage] = useState(
    "Using a selectable demo origin.",
  );
  const [locating, setLocating] = useState(false);
  const [selected, setSelected] = useState<number | null>(null);
  const [listOnly, setListOnly] = useState(false);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    api
      .stations(connector, query)
      .then((data) => {
        if (active) {
          setStations(data);
          setSelected(null);
        }
      })
      .catch((e) => {
        if (active) {
          setError(e.message);
          setStations([]);
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [api, connector, query, refresh]);
  function locate() {
    if (!navigator.geolocation) {
      setLocationMessage(
        "Location is unavailable. Choose a demo origin below.",
      );
      return;
    }
    setLocating(true);
    setLocationMessage("Waiting for location permission…");
    navigator.geolocation.getCurrentPosition(
      (p) => {
        setOrigin({
          latitude: p.coords.latitude,
          longitude: p.coords.longitude,
        });
        setLocationName("Browser location");
        setLocationMessage(
          "Using browser location for approximate straight-line distances.",
        );
        setLocating(false);
      },
      () => {
        setLocationMessage(
          "Location was denied, unavailable or timed out. Choose a demo origin below.",
        );
        setLocating(false);
      },
      { timeout: 10000, maximumAge: 60000, enableHighAccuracy: false },
    );
  }
  const sorted = stations
    .map((s) => ({ ...s, distance: distanceKm(origin, s) }))
    .sort((a, b) => a.distance - b.distance);
  return (
    <section className="mobility-page discovery">
      <div className="page-heading">
        <div>
          <div className="eyebrow muted">DHAKA–CHATTOGRAM CORRIDOR</div>
          <h1>Find your next charge.</h1>
          <p className="subtitle">
            Explore sample locations and compatible charging points.
          </p>
        </div>
        <span className="pill">DEMONSTRATION NETWORK</span>
      </div>
      <div className="discovery-controls panel">
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setQuery(search.trim());
          }}
        >
          <div>
            <label htmlFor="station-search">Search stations</label>
            <input
              id="station-search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              maxLength={120}
              placeholder="City, station or address"
            />
          </div>
          <div>
            <label htmlFor="station-connector">Connector</label>
            <select
              id="station-connector"
              value={connector}
              onChange={(e) => setConnector(e.target.value)}
            >
              <option value="">All connectors</option>
              {connectors.map((c) => (
                <option key={c}>{c}</option>
              ))}
            </select>
          </div>
          <button className="primary compact">Search</button>
        </form>
        <div className="location-controls">
          <div>
            <label htmlFor="demo-origin">Starting location</label>
            <select
              id="demo-origin"
              value={locationName}
              onChange={(e) => {
                const point = demoLocations.find(
                  (p) => p.name === e.target.value,
                )!;
                setOrigin(point);
                setLocationName(point.name);
                setLocationMessage("Using a selectable demo origin.");
              }}
            >
              {locationName === "Browser location" && (
                <option>Browser location</option>
              )}
              {demoLocations.map((p) => (
                <option key={p.name}>{p.name}</option>
              ))}
            </select>
          </div>
          <button className="secondary" onClick={locate} disabled={locating}>
            {locating ? "Locating…" : "Use my location"}
          </button>
          <p className="hint" role="status">
            {locationMessage}
          </p>
        </div>
      </div>
      <div className="discovery-summary">
        <p>
          {loading
            ? "Finding stations…"
            : `${stations.length} station${stations.length === 1 ? "" : "s"} found`}{" "}
          <span>· Approximate straight-line distance from {locationName}</span>
        </p>
        <button className="secondary" onClick={() => setListOnly((v) => !v)}>
          {listOnly ? "Show map" : "List only"}
        </button>
      </div>
      {loading ? (
        <p role="status" className="panel">
          Loading stations…
        </p>
      ) : error ? (
        <div className="panel">
          <p className="error" role="alert">
            {error}
          </p>
          <button
            className="secondary"
            onClick={() => setRefresh((r) => r + 1)}
          >
            Try again
          </button>
        </div>
      ) : stations.length === 0 ? (
        <div className="panel empty">
          <h2>No matching stations</h2>
          <p>
            Try another city or connector. Sample stations may need to be seeded
            by your demo administrator.
          </p>
        </div>
      ) : (
        <div
          className={listOnly ? "discovery-grid list-only" : "discovery-grid"}
        >
          {!listOnly && (
            <StationMap
              stations={stations}
              origin={origin}
              selected={selected}
              onSelect={setSelected}
            />
          )}
          <div className="station-list" aria-label="Station results">
            {sorted.map((s) => (
              <article
                className={`station-card ${s.id === selected ? "station-selected" : ""}`}
                key={s.id}
              >
                <div className="card-top">
                  <span className="pill">
                    {s.isDemo ? "SAMPLE LOCATION" : "STATION"}
                  </span>
                  <span className="distance">≈ {s.distance.toFixed(1)} km</span>
                </div>
                <h2>
                  <button
                    className="station-title"
                    onClick={() => setSelected(s.id)}
                  >
                    {s.name}
                  </button>
                </h2>
                <p className="station-address">{s.address}</p>
                {s.chargers.map((c) => (
                  <div className="charger-row" key={c.id}>
                    <div>
                      <strong>
                        {c.connectorType} · {c.powerKW} kW
                      </strong>
                      <span>
                        {c.chargerCode} · {c.pricePerKWh.toFixed(2)}{" "}
                        {c.currency}/kWh
                      </span>
                    </div>
                    <span
                      className={`availability ${c.isAvailable ? "available" : ""}`}
                    >
                      {c.effectiveStatus}
                    </span>
                  </div>
                ))}
                {s.chargers.some(
                  (c) => c.availabilitySource === "ConfiguredStatus",
                ) && (
                  <p className="hint">
                    Configured status snapshot · Reservation integration pending
                  </p>
                )}
                {s.chargers.some(
                  (c) => c.availabilitySource === "IntegrationRequired",
                ) && (
                  <p className="hint">
                    Live availability integration is required.
                  </p>
                )}
                <p className="booking-note">
                  Booking will connect here in the reservation module.
                </p>
              </article>
            ))}
          </div>
        </div>
      )}
      <p className="hint">
        Sample locations are not verified real charging stations. Distance is
        not road distance or driving ETA. Availability is informational; the
        booking API must validate it again.
      </p>
    </section>
  );
}
