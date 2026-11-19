// Game controller - Arduino Uno. Serial at 9600 baud.
//   Joystick : X -> A0, Y -> A2, SW -> D5 (to GND when pressed), +5V, GND
//   RGB LED  : D2 = red, D3 = green, D4 = blue, each through a resistor (about 220 ohm), common lead to GND (common cathode)
//   Button   : D7 to GND
//   Encoder  : CLK = A4, DT = A5, + to 5V, GND
//
// Controls:
//   Joystick move    left / right / up / down
//   Button (D7)      hold to go forward
//   Joystick press   eat (once per click)
//   Encoder          one click counter-clockwise (count -1) = LED dim, back at count 0 = LED bright
//
// Every message is one line of text, so the game only has to read lines from the serial port:
//   MOVE LEFT | MOVE RIGHT | MOVE UP | MOVE DOWN | MOVE NONE     sent when the joystick direction changes
//   FORWARD 1 | FORWARD 0                                         sent when the button is pressed / released
//   EAT                                                           sent once per joystick click
//   LIGHT BRIGHT | LIGHT DIM                                      sent when the encoder changes the LED

const long  BAUD             = 9600;
const int   COUNTS_PER_CLICK = 4;        // encoder counts per click: set 2 or 1 if one click does not move one step
const bool  ENC_REVERSE      = false;    // true if counter-clockwise should be bright instead of dim
const bool  COMMON_ANODE     = false;    // true if the LED's common lead goes to 5V
const bool  JOY_INVERT_X     = false;    // true if pushing right reads as left
const bool  JOY_INVERT_Y     = false;    // true if pushing up reads as down
const int   JOY_ON           = 250;      // distance from centre (0..512) to start a direction
const int   JOY_OFF          = 120;      // distance from centre to release it (lower than JOY_ON so it does not flicker)
const uint8_t LED_R = 1, LED_G = 1, LED_B = 1;   // LED colour: 1 = on, 0 = off (all on = white)
const uint8_t DUTY_BRIGHT    = 64;       // 0..64
const uint8_t DUTY_DIM       = 5;
const bool  DEBUG_STATUS     = false;    // true to also print a STATUS line every 100 ms (for testing in the Serial Monitor)

const uint8_t PIN_JX = A0, PIN_JY = A2, PIN_JSW = 5, PIN_BTN = 7;

enum Dir { DIR_NONE, DIR_LEFT, DIR_RIGHT, DIR_UP, DIR_DOWN };
const char *DIR_NAMES[5] = {"NONE", "LEFT", "RIGHT", "UP", "DOWN"};

struct Debounced {
  uint8_t pin;
  bool raw, stable;
  unsigned long changed;
};

Debounced btn = {PIN_BTN, false, false, 0};
Debounced jsw = {PIN_JSW, false, false, 0};

volatile long encCount = 0;
volatile uint8_t encState = 0;
volatile uint8_t dutyR = 0, dutyG = 0, dutyB = 0;        // 0..64
long lastC = 0;
int encPos = 0;                                          // 0 = bright, -1 = dim
Dir dir = DIR_NONE;
unsigned long lastPrint = 0;

ISR(PCINT1_vect) {                                       // encoder on A4 / A5
  static const int8_t T[16] = {0, -1, 1, 0, 1, 0, 0, -1, -1, 0, 0, 1, 0, 1, -1, 0};
  uint8_t p = PINC;
  encState = ((encState << 2) | (((p >> 4) & 1) << 1) | ((p >> 5) & 1)) & 0x0F;
  encCount += T[encState];
}

ISR(TIMER2_OVF_vect) {                                   // software PWM on D2, D3, D4 (PORTD bits 2, 3, 4)
  static uint8_t ph = 0;
  ph = (ph + 1) & 63;
  uint8_t m = 0;
  if (dutyR > ph) m |= _BV(2);
  if (dutyG > ph) m |= _BV(3);
  if (dutyB > ph) m |= _BV(4);
  if (COMMON_ANODE) m ^= 0x1C;
  PORTD = (PORTD & ~0x1C) | m;
}

long readCount() {
  noInterrupts();
  long c = encCount;
  interrupts();
  return ENC_REVERSE ? -c : c;
}

void applyLight() {
  bool dim = (encPos < 0);
  uint8_t d = dim ? DUTY_DIM : DUTY_BRIGHT;
  dutyR = LED_R ? d : 0;
  dutyG = LED_G ? d : 0;
  dutyB = LED_B ? d : 0;
  Serial.println(dim ? "LIGHT DIM" : "LIGHT BRIGHT");
}

// debounced input: returns true when the stable state just changed (read b.stable for the new state)
bool update(Debounced &b) {
  bool r = (digitalRead(b.pin) == LOW);
  unsigned long now = millis();
  if (r != b.raw) { b.raw = r; b.changed = now; }
  if (b.raw != b.stable && now - b.changed >= 25) {
    b.stable = b.raw;
    return true;
  }
  return false;
}

Dir readDirection(int x, int y) {
  int limit = (dir == DIR_NONE) ? JOY_ON : JOY_OFF;      // easier to keep a direction than to start one
  int ax = abs(x), ay = abs(y);
  if (max(ax, ay) < limit) return DIR_NONE;
  if (ax > ay) return (x < 0) ? DIR_LEFT : DIR_RIGHT;
  return (y < 0) ? DIR_UP : DIR_DOWN;
}

void setup() {
  Serial.begin(BAUD);
  pinMode(A4, INPUT_PULLUP);
  pinMode(A5, INPUT_PULLUP);
  pinMode(PIN_BTN, INPUT_PULLUP);
  pinMode(PIN_JSW, INPUT_PULLUP);
  DDRD |= 0x1C;                                          // D2, D3, D4 outputs
  encState = (((PINC >> 4) & 1) << 1) | ((PINC >> 5) & 1);
  PCICR |= (1 << PCIE1);
  PCMSK1 |= (1 << PCINT12) | (1 << PCINT13);             // only A4 and A5
  TCCR2A = 0;                                            // Timer2 normal mode, prescaler 8, overflow interrupt
  TCCR2B = (1 << CS21);
  TIMSK2 = (1 << TOIE2);
  lastC = readCount();
  Serial.println("READY");
  applyLight();
}

void loop() {
  // encoder: count 0 = bright, one click counter-clockwise (count -1) = dim
  long c = readCount();
  long diff = c - lastC;
  if (abs(diff) >= COUNTS_PER_CLICK) {
    long clicks = diff / COUNTS_PER_CLICK;
    lastC += clicks * COUNTS_PER_CLICK;
    int newPos = constrain(encPos + (int)clicks, -1, 0);
    if (newPos != encPos) {
      encPos = newPos;
      applyLight();
    }
  }

  // joystick move -> left / right / up / down
  int x = analogRead(PIN_JX) - 512;
  int y = analogRead(PIN_JY) - 512;
  if (JOY_INVERT_X) x = -x;
  if (JOY_INVERT_Y) y = -y;
  Dir d = readDirection(x, y);
  if (d != dir) {
    dir = d;
    Serial.print("MOVE ");
    Serial.println(DIR_NAMES[dir]);
  }

  // button -> forward while held
  if (update(btn)) {
    Serial.print("FORWARD ");
    Serial.println(btn.stable ? 1 : 0);
  }

  // joystick click -> eat
  if (update(jsw) && jsw.stable) {
    Serial.println("EAT");
  }

  if (DEBUG_STATUS && millis() - lastPrint >= 100) {
    lastPrint = millis();
    Serial.print("STATUS enc="); Serial.print(c);
    Serial.print(" pos="); Serial.print(encPos);
    Serial.print(" x="); Serial.print(x);
    Serial.print(" y="); Serial.print(y);
    Serial.print(" btn="); Serial.print(btn.stable ? 1 : 0);
    Serial.print(" sw="); Serial.println(jsw.stable ? 1 : 0);
  }
}

