import { useState, useCallback, useRef, useMemo, type ReactNode } from "react";
import { SKILL_TOOLTIPS } from "../shared/skillData";
import { COUNTRIES } from "../shared/countries";
import "./NewGameScreen.scss";
import backdropUrl from "../assets/track-backdrop.jpg";

// ---------------------------------------------------------------------------
// Logo SVGs (matching mockup exactly)
// ---------------------------------------------------------------------------

const LOGOS: { id: string; svg: ReactNode }[] = [
  {
    id: "shield",
    svg: (
      <svg viewBox="0 0 48 48">
        <path className="logo-icon" d="M24 4L40 12v14c0 12-16 18-16 18S8 38 8 26V12z" />
      </svg>
    ),
  },
  {
    id: "star",
    svg: (
      <svg viewBox="0 0 48 48">
        <polygon className="logo-icon" points="24,4 29.5,17.5 44,18 33,27.5 36,42 24,34 12,42 15,27.5 4,18 18.5,17.5" />
      </svg>
    ),
  },
  {
    id: "bolt",
    svg: (
      <svg viewBox="0 0 48 48">
        <polygon className="logo-icon" points="28,2 14,24 22,24 18,46 36,20 27,20" />
      </svg>
    ),
  },
  {
    id: "wings",
    svg: (
      <svg viewBox="0 0 48 48">
        <path className="logo-icon" d="M24 28C16 20 2 18 4 26s14 14 20 14 16-6 20-14-12-6-20 2z" />
      </svg>
    ),
  },
  {
    id: "flame",
    svg: (
      <svg viewBox="0 0 48 48">
        <path className="logo-icon" d="M24 2c0 0-16 18-16 30 0 8 7 14 16 14s16-6 16-14C40 20 24 2 24 2zM24 40c-5 0-8-3-8-8 0-6 8-16 8-16s8 10 8 16c0 5-3 8-8 8z" />
      </svg>
    ),
  },
  {
    id: "diamond",
    svg: (
      <svg viewBox="0 0 48 48">
        <rect className="logo-icon" x="14" y="14" width="20" height="20" rx="2" transform="rotate(45 24 24)" />
      </svg>
    ),
  },
  {
    id: "chevrons",
    svg: (
      <svg viewBox="0 0 48 48">
        <path className="logo-stroke" d="M10 8l14 16-14 16" strokeWidth="5" strokeLinecap="round" strokeLinejoin="round" />
        <path className="logo-stroke" d="M24 8l14 16-14 16" strokeWidth="5" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    ),
  },
  {
    id: "gear",
    svg: (
      <svg viewBox="0 0 48 48">
        <circle className="logo-stroke" cx="24" cy="24" r="7" strokeWidth="3.5" />
        <g className="logo-stroke" strokeWidth="3.5" strokeLinecap="round">
          <line x1="24" y1="4" x2="24" y2="11" />
          <line x1="24" y1="37" x2="24" y2="44" />
          <line x1="4" y1="24" x2="11" y2="24" />
          <line x1="37" y1="24" x2="44" y2="24" />
          <line x1="10" y1="10" x2="15" y2="15" />
          <line x1="33" y1="33" x2="38" y2="38" />
          <line x1="10" y1="38" x2="15" y2="33" />
          <line x1="33" y1="15" x2="38" y2="10" />
        </g>
      </svg>
    ),
  },
  {
    id: "target",
    svg: (
      <svg viewBox="0 0 48 48">
        <circle className="logo-stroke" cx="24" cy="24" r="18" strokeWidth="3" />
        <circle className="logo-stroke" cx="24" cy="24" r="10" strokeWidth="3" />
        <circle className="logo-icon" cx="24" cy="24" r="4" />
      </svg>
    ),
  },
  {
    id: "custom",
    svg: (
      <svg viewBox="0 0 48 48">
        <line className="logo-stroke" x1="24" y1="12" x2="24" y2="36" strokeWidth="3.5" strokeLinecap="round" />
        <line className="logo-stroke" x1="12" y1="24" x2="36" y2="24" strokeWidth="3.5" strokeLinecap="round" />
      </svg>
    ),
  },
];

// ---------------------------------------------------------------------------
// Skill definitions
// ---------------------------------------------------------------------------

const SKILL_DEFS = [
  { key: "driver" as const, label: "Driver" },
  { key: "engineer" as const, label: "Engineer" },
  { key: "business" as const, label: "Business" },
];

const MAX_SKILL = 20;
const TOTAL_POINTS = 15;

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

interface NewGameScreenProps {
  onStart: (data: {
    playerName: string;
    nationality: string;
    teamName: string;
    logo: string | null;
    skills: { driver: number; engineer: number; business: number };
  }) => void;
}

export function NewGameScreen({ onStart }: NewGameScreenProps) {
  const [step, setStep] = useState(1);
  const [playerName, setPlayerName] = useState("");
  const [nationalityCode, setNationalityCode] = useState("");
  const [nationalitySearch, setNationalitySearch] = useState("");
  const [nationalityOpen, setNationalityOpen] = useState(false);
  const [teamName, setTeamName] = useState("");
  const [selectedLogo, setSelectedLogo] = useState("shield");
  const [skills, setSkills] = useState({ driver: 0, engineer: 0, business: 0 });
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [customLogoDataUrl, setCustomLogoDataUrl] = useState<string | null>(null);

  const remaining = TOTAL_POINTS - skills.driver - skills.engineer - skills.business;
  const step1Valid = playerName.trim().length > 0 && nationalityCode.length > 0 && teamName.trim().length > 0;

  const filteredCountries = useMemo(() => {
    if (!nationalitySearch) return COUNTRIES;
    const q = nationalitySearch.toLowerCase();
    return COUNTRIES.filter((c) => c.name.toLowerCase().includes(q) || c.code.includes(q));
  }, [nationalitySearch]);

  const selectedCountry = COUNTRIES.find((c) => c.code === nationalityCode);

  const adjustSkill = useCallback(
    (key: "driver" | "engineer" | "business", delta: number) => {
      setSkills((prev) => {
        const newVal = prev[key] + delta;
        const newRemaining = TOTAL_POINTS - (prev.driver + prev.engineer + prev.business) - delta;
        if (newVal < 0 || newVal > MAX_SKILL || newRemaining < 0) return prev;
        return { ...prev, [key]: newVal };
      });
    },
    [],
  );

  const handleLogoClick = useCallback(
    (id: string) => {
      if (id === "custom") {
        fileInputRef.current?.click();
      } else {
        setSelectedLogo(id);
        setCustomLogoDataUrl(null);
      }
    },
    [],
  );

  const handleFileUpload = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0];
      if (!file) return;
      const reader = new FileReader();
      reader.onload = () => {
        setCustomLogoDataUrl(reader.result as string);
        setSelectedLogo("custom");
      };
      reader.readAsDataURL(file);
    },
    [],
  );

  const handleStart = useCallback(() => {
    const logo = selectedLogo === "custom" ? customLogoDataUrl : selectedLogo;
    onStart({
      playerName: playerName.trim(),
      nationality: nationalityCode,
      teamName: teamName.trim(),
      logo,
      skills,
    });
  }, [onStart, playerName, nationalityCode, teamName, selectedLogo, customLogoDataUrl, skills]);

  // --- Step tabs ---
  const stepTab = (n: number, label: string) => {
    const cls = n === step ? "active" : n < step ? "done" : "";
    return (
      <div className={`step-tab ${cls}`} key={n}>
        <span className="num">{n < step ? "\u2713" : n}</span>
        <span className="lab">{label}</span>
      </div>
    );
  };

  return (
    <div
      className="new-game-root"
      style={{ "--backdrop-url": `url(${backdropUrl})` } as React.CSSProperties}
    >
      <div className="screen">
        {/* Title */}
        <div className="title-area">
          <h1>
            THE <span className="accent">24H</span> TARMAC GAUNTLET
          </h1>
          <div className="title-divider" />
          <p className="subtitle">New Career</p>
        </div>

        {/* Wizard */}
        <div className="wizard">
          {/* Step tabs */}
          <div className="steps-indicator">
            {stepTab(1, "Identity")}
            {stepTab(2, "Team Logo")}
            {stepTab(3, "Skills")}
          </div>

          {/* Step 1 — Identity */}
          {step === 1 && (
            <div className="step-content">
              <div className="field-group">
                <label className="field-label">Your Name</label>
                <input
                  className="field-input"
                  type="text"
                  value={playerName}
                  onChange={(e) => setPlayerName(e.target.value)}
                  placeholder="Enter your name"
                  autoComplete="off"
                  spellCheck={false}
                />
              </div>
              <div className="field-group">
                <label className="field-label">Nationality</label>
                <div className="nationality-wrap">
                  {selectedCountry && (
                    <span className={`fi fi-${selectedCountry.code} nationality-flag-preview`} />
                  )}
                  <input
                    className="nationality-input"
                    type="text"
                    value={nationalityOpen ? nationalitySearch : selectedCountry?.name ?? ""}
                    onChange={(e) => { setNationalitySearch(e.target.value); setNationalityOpen(true); }}
                    onFocus={() => { setNationalityOpen(true); setNationalitySearch(""); }}
                    onBlur={() => setTimeout(() => setNationalityOpen(false), 150)}
                    placeholder="Search country..."
                    autoComplete="off"
                    spellCheck={false}
                    style={{ paddingLeft: selectedCountry ? 46 : 18 }}
                  />
                  {nationalityOpen && filteredCountries.length > 0 && (
                    <div className="nationality-dropdown">
                      {filteredCountries.map((c) => (
                        <div
                          key={c.code}
                          className={`nationality-option ${c.code === nationalityCode ? "selected" : ""}`}
                          onMouseDown={() => {
                            setNationalityCode(c.code);
                            setNationalitySearch("");
                            setNationalityOpen(false);
                          }}
                        >
                          <span className={`fi fi-${c.code}`} style={{ marginRight: 8, borderRadius: 2 }} />
                          {c.name}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
              <div className="field-group">
                <label className="field-label">Team Name</label>
                <input
                  className="field-input"
                  type="text"
                  value={teamName}
                  onChange={(e) => setTeamName(e.target.value)}
                  placeholder="Enter team name"
                  autoComplete="off"
                  spellCheck={false}
                />
              </div>
            </div>
          )}

          {/* Step 2 — Logo */}
          {step === 2 && (
            <div className="step-content">
              <p className="section-label">Choose Your Logo</p>
              <div className="logo-grid">
                {LOGOS.map((logo) => (
                  <div
                    key={logo.id}
                    className={`logo-tile ${selectedLogo === logo.id ? "selected" : ""}`}
                    onClick={() => handleLogoClick(logo.id)}
                  >
                    {logo.id === "custom" && customLogoDataUrl ? (
                      <img
                        src={customLogoDataUrl}
                        alt="Custom logo"
                        style={{ width: 48, height: 48, objectFit: "contain" }}
                      />
                    ) : (
                      logo.svg
                    )}
                    {logo.id === "custom" && <span className="upload-text">Custom</span>}
                  </div>
                ))}
              </div>
              <input
                ref={fileInputRef}
                type="file"
                accept="image/*"
                style={{ display: "none" }}
                onChange={handleFileUpload}
              />
            </div>
          )}

          {/* Step 3 — Skills */}
          {step === 3 && (
            <div className="step-content">
              <div className="points-header">
                <span className="section-label">Allocate Skill Points</span>
                <span className={`points-remaining ${remaining === 0 ? "zero" : ""}`}>
                  <span className="pts-num">{remaining}</span> remaining
                </span>
              </div>

              {SKILL_DEFS.map((skill) => (
                <div className="skill-row" key={skill.key}>
                  <div className="skill-info">
                    <span className="skill-name">{skill.label}</span>
                    <span className="info-dot">
                      i<span className="tip">{SKILL_TOOLTIPS[skill.key]}</span>
                    </span>
                  </div>
                  <div className="skill-controls">
                    <button
                      className="sk-btn"
                      disabled={skills[skill.key] <= 0}
                      onClick={() => adjustSkill(skill.key, -1)}
                    >
                      &minus;
                    </button>
                    <span className="sk-val">{skills[skill.key]}</span>
                    <button
                      className="sk-btn"
                      disabled={skills[skill.key] >= MAX_SKILL || remaining <= 0}
                      onClick={() => adjustSkill(skill.key, 1)}
                    >
                      +
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          {/* Navigation */}
          <div className="nav-buttons">
            {step > 1 ? (
              <button className="btn btn-back" onClick={() => setStep(step - 1)}>
                Back
              </button>
            ) : (
              <div className="spacer" />
            )}

            {step < 3 ? (
              <button
                className="btn btn-primary"
                disabled={step === 1 && !step1Valid}
                onClick={() => setStep(step + 1)}
              >
                Next
              </button>
            ) : (
              <button
                className="btn btn-primary btn-start"
                disabled={remaining !== 0}
                onClick={handleStart}
              >
                Start Career
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
