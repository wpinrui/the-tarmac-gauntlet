/** Country entry for the nationality selector. Code is ISO 3166-1 alpha-2 lowercase. */
export interface Country {
  code: string;
  name: string;
}

/** Common racing nations first, then alphabetical remainder. */
export const COUNTRIES: Country[] = [
  { code: "gb", name: "United Kingdom" },
  { code: "us", name: "United States" },
  { code: "de", name: "Germany" },
  { code: "it", name: "Italy" },
  { code: "fr", name: "France" },
  { code: "es", name: "Spain" },
  { code: "br", name: "Brazil" },
  { code: "jp", name: "Japan" },
  { code: "au", name: "Australia" },
  { code: "nl", name: "Netherlands" },
  { code: "fi", name: "Finland" },
  { code: "se", name: "Sweden" },
  { code: "dk", name: "Denmark" },
  { code: "no", name: "Norway" },
  { code: "at", name: "Austria" },
  { code: "ch", name: "Switzerland" },
  { code: "be", name: "Belgium" },
  { code: "ca", name: "Canada" },
  { code: "mx", name: "Mexico" },
  { code: "ar", name: "Argentina" },
  { code: "cl", name: "Chile" },
  { code: "co", name: "Colombia" },
  { code: "ve", name: "Venezuela" },
  { code: "kr", name: "South Korea" },
  { code: "cn", name: "China" },
  { code: "tw", name: "Taiwan" },
  { code: "in", name: "India" },
  { code: "sg", name: "Singapore" },
  { code: "my", name: "Malaysia" },
  { code: "th", name: "Thailand" },
  { code: "ph", name: "Philippines" },
  { code: "nz", name: "New Zealand" },
  { code: "za", name: "South Africa" },
  { code: "ie", name: "Ireland" },
  { code: "pt", name: "Portugal" },
  { code: "pl", name: "Poland" },
  { code: "cz", name: "Czech Republic" },
  { code: "hu", name: "Hungary" },
  { code: "ru", name: "Russia" },
  { code: "gh", name: "Ghana" },
];
