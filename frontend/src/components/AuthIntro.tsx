export function Intro({ signup = false }: { signup?: boolean }) {
  return (
    <aside className="intro">
      <div className="eyebrow">
        <span className="status-dot" /> THE ROAD AHEAD IS ELECTRIC
      </div>
      <h1>
        {signup ? (
          <>
            Your next journey
            <br />
            starts here.
          </>
        ) : (
          <>
            A fresh charge.
            <br />A better journey.
          </>
        )}
      </h1>
      <p className="lead">
        One account to connect your electric journeys. Built for owners. Ready
        for drivers.
      </p>
      <div className="journey" aria-hidden="true">
        <div className="journey-line" />
        <span className="journey-point start">DHAKA</span>
        <div className="charge-symbol">ϟ</div>
        <span className="journey-point end">CHATTOGRAM</span>
      </div>
      <div className="intro-bottom">
        <span className="mini-icon" aria-hidden="true">
          ↗
        </span>
        <div>
          <strong>Connected from the start</strong>
          <p>Discover, reserve and charge — the project vision.</p>
        </div>
      </div>
      <p className="disclaimer">
        Demo vehicles and sample station discovery are ready. Charging and
        payments will connect next.
      </p>
    </aside>
  );
}
